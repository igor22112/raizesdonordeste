using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Estoques;
using RaizesDoNordeste.Application.Pedidos;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.Application.Pagamentos;

public sealed record SolicitarPagamentoRequest(MetodoPagamento Metodo);

/// <summary>Notificação assíncrona (webhook) enviada pelo gateway.</summary>
/// <param name="Referencia">Id do Pagamento enviado na solicitação.</param>
/// <param name="Status">"aprovado" ou "recusado".</param>
public sealed record NotificacaoPagamento(Guid Referencia, string TransacaoId, string Status, string? Motivo);

public sealed class SolicitarPagamentoValidator : AbstractValidator<SolicitarPagamentoRequest>
{
    public SolicitarPagamentoValidator() => RuleFor(x => x.Metodo).IsInEnum();
}

public sealed class NotificacaoPagamentoValidator : AbstractValidator<NotificacaoPagamento>
{
    public NotificacaoPagamentoValidator()
    {
        RuleFor(x => x.Referencia).NotEmpty();
        RuleFor(x => x.TransacaoId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).NotEmpty().Must(s => s is "aprovado" or "recusado").WithMessage("Status deve ser 'aprovado' ou 'recusado'.");
        RuleFor(x => x.Motivo).MaximumLength(300);
    }
}

/// <summary>
/// Assinatura HMAC-SHA256 do webhook: o gateway assina "{timestamp}.{corpo}" com um segredo
/// compartilhado. Validar a assinatura impede que terceiros confirmem pagamentos falsos;
/// validar o timestamp limita ataques de repetição (replay).
/// </summary>
public static class AssinaturaWebhook
{
    public static string Calcular(string segredo, string timestamp, string corpo)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes($"{timestamp}.{corpo}"));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    public static bool Validar(string segredo, string? timestamp, string? assinatura, string corpo, DateTimeOffset agora, TimeSpan tolerancia)
    {
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(assinatura)) return false;
        if (!long.TryParse(timestamp, out var unix)) return false;
        var enviadoEm = DateTimeOffset.FromUnixTimeSeconds(unix);
        if ((agora - enviadoEm).Duration() > tolerancia) return false;

        var esperado = Encoding.UTF8.GetBytes(Calcular(segredo, timestamp, corpo));
        var recebido = Encoding.UTF8.GetBytes(assinatura);
        // Comparação em tempo constante: não vaza, pelo tempo de resposta, quantos bytes conferem.
        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }
}

public sealed class PagamentoService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    IGatewayPagamento gateway,
    TimeProvider relogio,
    EstoqueOperacoes estoque,
    EstornoPagamentos estornos,
    IAuditoria auditoria,
    IValidator<SolicitarPagamentoRequest> validadorSolicitacao,
    IValidator<NotificacaoPagamento> validadorNotificacao,
    ILogger<PagamentoService> logger)
{
    public async Task<PagamentoDto> SolicitarAsync(Guid pedidoId, SolicitarPagamentoRequest req, CancellationToken ct)
    {
        await validadorSolicitacao.ValidateAndThrowAsync(req, ct);
        var agora = relogio.GetUtcNow();
        var pedido = await db.Pedidos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pedidoId, ct)
            ?? throw new NaoEncontradoException("Pedido");
        GarantirPodePagar(pedido);

        if (pedido.Status != StatusPedido.AguardandoPagamento)
            throw new DomainException("PEDIDO_NAO_AGUARDA_PAGAMENTO", $"O pedido está {pedido.Status} e não aceita pagamento.");
        if (pedido.PrazoPagamentoVencido(agora))
            throw new DomainException("PRAZO_PAGAMENTO_ESGOTADO", "O prazo para pagamento deste pedido terminou.");

        var pagamento = await db.Pagamentos
            .Where(p => p.PedidoId == pedidoId && (p.Status == StatusPagamento.Pendente || p.Status == StatusPagamento.Aprovado))
            .FirstOrDefaultAsync(ct);

        if (pagamento?.Status == StatusPagamento.Aprovado)
            throw new ConflitoException("PAGAMENTO_JA_APROVADO", "Este pedido já foi pago.");
        if (pagamento is { TransacaoExternaId: not null })
            return ParaDto(pagamento); // já enviado ao gateway: não cobra de novo

        if (pagamento is null)
        {
            pagamento = new Pagamento(pedido.Id, pedido.Total, req.Metodo, agora);
            db.Pagamentos.Add(pagamento);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Pagamentos_PedidoId_Ativo") == true)
            {
                // Outra requisição criou o pagamento ao mesmo tempo; o índice único impede cobrança dupla.
                db.LimparRastreamento();
                var existente = await db.Pagamentos.AsNoTracking()
                    .FirstAsync(p => p.PedidoId == pedidoId && (p.Status == StatusPagamento.Pendente || p.Status == StatusPagamento.Aprovado), ct);
                return ParaDto(existente);
            }
        }

        // Pagamento novo, ou pendente sem transação (falha entre gravar e chamar o gateway):
        // reenviamos com a MESMA chave de idempotência (Id do pagamento), sem risco de cobrança dupla.
        try
        {
            var resposta = await gateway.SolicitarAsync(new SolicitacaoPagamento(pagamento.Id, pedido.Id, pagamento.Valor, pagamento.Metodo), ct);
            if (resposta.Aceita) pagamento.RegistrarEnvio(resposta.TransacaoId!, relogio.GetUtcNow());
            else pagamento.Recusar(resposta.MotivoRecusa, relogio.GetUtcNow());
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outra requisição (ou o próprio webhook, se foi muito rápido) já atualizou este pagamento.
                // O gateway devolveu a mesma transação graças à chave de idempotência; basta reler o estado atual.
                db.LimparRastreamento();
                return ParaDto(await db.Pagamentos.AsNoTracking().FirstAsync(p => p.Id == pagamento.Id, ct));
            }
            return ParaDto(pagamento);
        }
        catch (GatewayIndisponivelException ex)
        {
            logger.LogError(ex, "Gateway de pagamento indisponível para o pagamento {PagamentoId}", pagamento.Id);
            pagamento.MarcarFalha("Gateway de pagamento indisponível.", relogio.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
            throw new ServicoIndisponivelException("O serviço de pagamento está temporariamente indisponível. Tente novamente em instantes.", 15);
        }
    }

    /// <summary>
    /// Processa o webhook do gateway. É idempotente: notificações repetidas para um pagamento já
    /// finalizado são ignoradas (gateways reenviam até receber 2xx).
    /// </summary>
    public async Task ProcessarNotificacaoAsync(NotificacaoPagamento n, CancellationToken ct)
    {
        await validadorNotificacao.ValidateAndThrowAsync(n, ct);

        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                var estornar = await db.EmTransacaoAsync(async c =>
                {
                    var agora = relogio.GetUtcNow();
                    var pagamento = await db.Pagamentos.FirstOrDefaultAsync(p => p.Id == n.Referencia, c)
                        ?? throw new NaoEncontradoException("Pagamento");

                    if (pagamento.TransacaoExternaId is not null && pagamento.TransacaoExternaId != n.TransacaoId)
                        throw new DomainException("TRANSACAO_DIVERGENTE", "A transação informada não corresponde ao pagamento.");

                    if (pagamento.Status != StatusPagamento.Pendente)
                    {
                        logger.LogInformation("Notificação repetida para o pagamento {PagamentoId} ({Status}); ignorada.", pagamento.Id, pagamento.Status);
                        return false;
                    }

                    if (n.Status == "recusado")
                    {
                        pagamento.Recusar(n.Motivo ?? "Recusado pelo emissor.", agora);
                        await db.SaveChangesAsync(c);
                        return false;
                    }

                    pagamento.Aprovar(n.TransacaoId, agora);
                    var pedido = await db.Pedidos.Include(p => p.Itens).FirstAsync(p => p.Id == pagamento.PedidoId, c);
                    var precisaEstorno = pedido.Status != StatusPedido.AguardandoPagamento;
                    if (!precisaEstorno)
                    {
                        pedido.ConfirmarPagamento(agora);
                        auditoria.Registrar(AcoesAuditoria.PedidoStatusAlterado, nameof(Pedido), pedido.Id,
                            new { De = StatusPedido.AguardandoPagamento, Para = StatusPedido.Pago, Origem = "Gateway de pagamento" }, pedido.UnidadeId);
                        await estoque.BaixarAsync(pedido.UnidadeId, pedido.Id,
                            pedido.Itens.Select(i => new ItemMovimentado(i.ProdutoId, i.NomeProduto, i.Quantidade)), c);
                    }
                    else
                    {
                        logger.LogWarning("Pagamento {PagamentoId} aprovado para pedido {PedidoId} já {Status}; será estornado.",
                            pagamento.Id, pedido.Id, pedido.Status);
                    }
                    await db.SaveChangesAsync(c);
                    return precisaEstorno;
                }, ct);

                if (estornar)
                    await estornos.EstornarAsync(n.Referencia, "Pagamento aprovado após o pedido ser cancelado ou expirar.", ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (tentativa < 3)
            {
                // Pedido/pagamento alterado em paralelo (ex.: expiração ou webhook duplicado). Relê e reaplica.
                db.LimparRastreamento();
            }
        }
    }

    private void GarantirPodePagar(Pedido p)
    {
        var permitido = usuario.Perfil switch
        {
            Perfil.Cliente => p.ClienteId == usuario.Id,
            Perfil.Totem => p.CriadoPorUsuarioId == usuario.Id,
            Perfil.Atendente or Perfil.Gerente => p.UnidadeId == usuario.UnidadeId,
            _ => false
        };
        if (!permitido) throw new NaoEncontradoException("Pedido");
    }

    internal static PagamentoDto ParaDto(Pagamento p) =>
        new(p.Id, p.PedidoId, p.Metodo, p.Status, p.Valor, p.Detalhe, p.CriadoEm, p.AtualizadoEm);
}

/// <summary>Solicita estorno ao gateway e registra o resultado (com auditoria).</summary>
public sealed class EstornoPagamentos(
    IAppDbContext db,
    IGatewayPagamento gateway,
    IAuditoria auditoria,
    TimeProvider relogio,
    ILogger<EstornoPagamentos> logger)
{
    public async Task EstornarAsync(Guid pagamentoId, string motivo, CancellationToken ct)
    {
        var pagamento = await db.Pagamentos.FirstOrDefaultAsync(p => p.Id == pagamentoId, ct);
        if (pagamento is null || pagamento.Status != StatusPagamento.Aprovado) return;

        try
        {
            await gateway.EstornarAsync(pagamento.TransacaoExternaId!, pagamento.Id, ct);
            pagamento.Estornar(relogio.GetUtcNow());
        }
        catch (GatewayIndisponivelException ex)
        {
            // O cancelamento não é desfeito: a falha do estorno fica registrada (status EstornoPendente) e auditada.
            logger.LogError(ex, "Falha ao estornar o pagamento {PagamentoId}; marcado como EstornoPendente.", pagamento.Id);
            pagamento.MarcarEstornoPendente("Gateway indisponível no estorno.", relogio.GetUtcNow());
        }

        var unidadeId = await db.Pedidos.Where(p => p.Id == pagamento.PedidoId).Select(p => p.UnidadeId).FirstAsync(ct);
        auditoria.Registrar(AcoesAuditoria.PagamentoEstornado, nameof(Pagamento), pagamento.Id,
            new { pagamento.PedidoId, pagamento.Valor, Resultado = pagamento.Status, Motivo = motivo }, unidadeId);
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
