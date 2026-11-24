using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Infrastructure.Seguranca;
using Serilog.Context;

namespace RaizesDoNordeste.Api.Infra;

/// <summary>Nomes de perfis para [Authorize(Roles = ...)].</summary>
public static class Perfis
{
    public const string Admin = nameof(Perfil.AdminMatriz);
    public const string Gerente = nameof(Perfil.Gerente);
    public const string Atendente = nameof(Perfil.Atendente);
    public const string Cozinha = nameof(Perfil.Cozinha);
    public const string Totem = nameof(Perfil.Totem);
    public const string Cliente = nameof(Perfil.Cliente);

    public const string EquipeLoja = Gerente + "," + Atendente + "," + Cozinha;
    public const string GerenciaOuAdmin = Gerente + "," + Admin;
    public const string QuemFazPedido = Cliente + "," + Totem + "," + Atendente + "," + Gerente;
}

internal sealed class UsuarioAtualHttp(IHttpContextAccessor acessor) : IUsuarioAtual
{
    private ClaimsPrincipal? Principal => acessor.HttpContext?.User is { Identity.IsAuthenticated: true } u ? u : null;

    public Guid? Id => Guid.TryParse(Principal?.FindFirstValue(ClaimsApp.Sub), out var id) ? id : null;
    public Perfil? Perfil => Enum.TryParse<Perfil>(Principal?.FindFirstValue(ClaimsApp.Perfil), out var p) ? p : null;
    public Guid? UnidadeId => Guid.TryParse(Principal?.FindFirstValue(ClaimsApp.UnidadeId), out var u) ? u : null;
    public string? Ip => acessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelacaoId => acessor.HttpContext?.Items[CorrelacaoMiddleware.Chave] as string;
}

/// <summary>
/// Propaga o header X-Correlation-Id (ou cria um) para logs, respostas e registros de auditoria,
/// permitindo seguir uma requisição entre API, gateway e jobs.
/// </summary>
internal sealed class CorrelacaoMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";
    public const string Chave = "CorrelacaoId";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) id = ctx.TraceIdentifier;
        ctx.Items[Chave] = id;
        // OnStarting: o tratador de exceções limpa os headers antes de escrever o erro; assim o id sempre volta.
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });
        using (LogContext.PushProperty("CorrelacaoId", id))
        {
            await next(ctx);
        }
    }
}

/// <summary>
/// Converte exceções em respostas ProblemDetails (RFC 9457) com um código estável em "codigo".
/// Erros inesperados retornam 500 sem detalhes internos (evita vazar stack trace).
/// </summary>
internal sealed class ManipuladorExcecoes(IProblemDetailsService problemDetails, ILogger<ManipuladorExcecoes> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, titulo, codigo, detalhe) = ex switch
        {
            ValidationException v => (400, "Dados inválidos", "VALIDACAO", v.Errors.Any() ? "Um ou mais campos são inválidos." : v.Message),
            DomainException d => (409, "Regra de negócio violada", d.Codigo, d.Message),
            NaoEncontradoException => (404, "Não encontrado", "NAO_ENCONTRADO", ex.Message),
            AcessoNegadoException => (403, "Acesso negado", "ACESSO_NEGADO", ex.Message),
            NaoAutenticadoException => (401, "Não autenticado", "NAO_AUTENTICADO", ex.Message),
            ConflitoException c => (409, "Conflito", c.Codigo, c.Message),
            DbUpdateConcurrencyException => (409, "Conflito de concorrência", "CONFLITO_CONCORRENCIA",
                "O registro foi alterado por outra operação ao mesmo tempo. Consulte novamente e repita a ação."),
            ServicoIndisponivelException s => (503, "Serviço indisponível", "SERVICO_INDISPONIVEL", s.Message),
            BadHttpRequestException b => (b.StatusCode, "Requisição inválida", "REQUISICAO_INVALIDA", "Requisição malformada."),
            _ => (500, "Erro interno", "ERRO_INTERNO", "Ocorreu um erro inesperado. Informe o correlacaoId ao suporte.")
        };

        if (status >= 500 && status != 503) logger.LogError(ex, "Erro não tratado");
        else logger.LogInformation("Requisição rejeitada com {Status} ({Codigo}): {Mensagem}", status, codigo, ex.Message);

        if (ex is ServicoIndisponivelException indisponivel)
            ctx.Response.Headers.RetryAfter = indisponivel.RetryAfterSegundos.ToString();

        ctx.Response.StatusCode = status;
        var problema = new ProblemDetails { Status = status, Title = titulo, Detail = detalhe, Type = $"https://httpstatuses.io/{status}", Instance = ctx.Request.Path };
        problema.Extensions["codigo"] = codigo;
        if (ex is ValidationException val && val.Errors.Any())
        {
            problema.Extensions["erros"] = val.Errors
                .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "geral" : e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = problema, Exception = ex });
    }
}

internal sealed class DocumentacaoOpenApi : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Raízes do Nordeste — API",
            Version = "v1",
            Description = "API back-end da rede Raízes do Nordeste: cardápio por unidade, pedidos multicanal, estoque, " +
                          "pagamento via gateway externo, fidelidade com LGPD, auditoria e relatórios para a matriz. " +
                          "Autentique em POST /api/v1/auth/login e use o token no botão de autenticação (Bearer)."
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token JWT obtido em /api/v1/auth/login"
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
