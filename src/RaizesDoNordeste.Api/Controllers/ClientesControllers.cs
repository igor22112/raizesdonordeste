using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaizesDoNordeste.Api.Infra;
using RaizesDoNordeste.Application.Campanhas;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Fidelidade;
using RaizesDoNordeste.Application.Privacidade;
using RaizesDoNordeste.Application.Relatorios;

namespace RaizesDoNordeste.Api.Controllers;

[ApiController]
[Route("api/v1/clientes/me")]
[Authorize(Roles = Perfis.Cliente)]
[Produces("application/json")]
[Tags("Cliente: fidelidade e privacidade (LGPD)")]
public sealed class MeuPerfilController(FidelidadeService fidelidade, PrivacidadeService privacidade, CampanhaService campanhas) : ControllerBase
{
    /// <summary>Saldo de pontos, nível (Bronze/Prata/Ouro), desconto progressivo e extrato recente.</summary>
    [HttpGet("fidelidade")]
    public Task<FidelidadeDto> Fidelidade(CancellationToken ct) => fidelidade.ObterResumoAsync(ct);

    /// <summary>Campanhas vigentes para as quais o cliente é elegível (opcionalmente para uma unidade).</summary>
    [HttpGet("campanhas")]
    public Task<IReadOnlyList<CampanhaDto>> Campanhas([FromQuery] Guid? unidadeId, CancellationToken ct) =>
        campanhas.ListarElegiveisAsync(unidadeId, ct);

    [HttpGet("consentimentos")]
    public Task<IReadOnlyList<ConsentimentoDto>> Consentimentos(CancellationToken ct) => privacidade.ObterConsentimentosAsync(ct);

    /// <summary>Concede ou revoga um consentimento. Cada alteração gera um novo registro (histórico comprobatório).</summary>
    [HttpPut("consentimentos")]
    public Task<ConsentimentoDto> AlterarConsentimento(AlterarConsentimentoRequest req, CancellationToken ct) =>
        privacidade.AlterarConsentimentoAsync(req, ct);

    /// <summary>Portabilidade/acesso (LGPD art. 18, II e V): exporta todos os dados pessoais do titular.</summary>
    [HttpGet("dados")]
    public Task<DadosTitularDto> ExportarDados(CancellationToken ct) => privacidade.ExportarDadosAsync(ct);

    /// <summary>Eliminação (LGPD art. 18, VI): anonimiza a conta. Pedidos são mantidos sem dados pessoais.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ExcluirConta(CancellationToken ct)
    {
        await privacidade.AnonimizarContaAsync(ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/clientes")]
[Authorize(Roles = Perfis.Admin)]
[Tags("Cliente: fidelidade e privacidade (LGPD)")]
public sealed class ClientesAdminController(PrivacidadeService privacidade) : ControllerBase
{
    /// <summary>Consulta administrativa de dados pessoais. Todo acesso é registrado na auditoria.</summary>
    [HttpGet("{id:guid}")]
    public Task<ClienteAdminDto> Obter(Guid id, CancellationToken ct) => privacidade.ObterClienteComoAdminAsync(id, ct);
}

[ApiController]
[Route("api/v1/campanhas")]
[Authorize(Roles = Perfis.Admin)]
[Produces("application/json")]
[Tags("Campanhas")]
public sealed class CampanhasController(CampanhaService servico) : ControllerBase
{
    /// <summary>Cria campanha. PercentualDesconto em % (1 a 30). Segmentação por frequência, nível e região é opcional.</summary>
    [HttpPost]
    [ProducesResponseType<CampanhaDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CampanhaDto>> Criar(CriarCampanhaRequest req, CancellationToken ct)
    {
        var c = await servico.CriarAsync(req, ct);
        return StatusCode(StatusCodes.Status201Created, c);
    }

    [HttpGet]
    public Task<IReadOnlyList<CampanhaDto>> Listar([FromQuery] bool apenasVigentes, CancellationToken ct) => servico.ListarAsync(apenasVigentes, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct)
    {
        await servico.DesativarAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/relatorios")]
[Authorize(Roles = Perfis.GerenciaOuAdmin)]
[Produces("application/json")]
[Tags("Relatórios da matriz")]
public sealed class RelatoriosController(RelatorioService servico) : ControllerBase
{
    /// <summary>Vendas consolidadas por unidade, região ou canal. Gerentes veem apenas a própria unidade.</summary>
    [HttpGet("vendas")]
    public Task<RelatorioVendasDto> Vendas([FromQuery] DateOnly de, [FromQuery] DateOnly ate, [FromQuery] AgrupamentoVendas agrupamento,
        [FromQuery] Guid? unidadeId, CancellationToken ct) =>
        servico.VendasAsync(new FiltroRelatorio(de, ate, unidadeId), agrupamento, ct);

    [HttpGet("produtos-mais-vendidos")]
    public Task<IReadOnlyList<ProdutoMaisVendidoDto>> MaisVendidos([FromQuery] DateOnly de, [FromQuery] DateOnly ate, [FromQuery] Guid? unidadeId,
        [FromQuery] int top = 10, CancellationToken ct = default) =>
        servico.ProdutosMaisVendidosAsync(new FiltroRelatorio(de, ate, unidadeId), top, ct);

    /// <summary>Contagem de operações sensíveis (cancelamentos, descontos, ajustes de estoque, acessos a dados pessoais).</summary>
    [HttpGet("operacoes-sensiveis")]
    public Task<IReadOnlyList<ResumoAuditoriaDto>> OperacoesSensiveis([FromQuery] DateOnly de, [FromQuery] DateOnly ate, [FromQuery] Guid? unidadeId,
        CancellationToken ct) =>
        servico.OperacoesSensiveisAsync(new FiltroRelatorio(de, ate, unidadeId), ct);
}

[ApiController]
[Route("api/v1/auditoria")]
[Authorize(Roles = Perfis.Admin)]
[Produces("application/json")]
[Tags("Auditoria")]
public sealed class AuditoriaController(RelatorioService servico) : ControllerBase
{
    /// <summary>Trilha de auditoria (somente leitura).</summary>
    [HttpGet]
    public Task<ResultadoPaginado<RegistroAuditoriaDto>> Listar([FromQuery] string? acao, [FromQuery] string? entidade, [FromQuery] string? entidadeId,
        [FromQuery] Guid? unidadeId, [FromQuery] DateTimeOffset? de, [FromQuery] DateTimeOffset? ate, [FromQuery] int? page, [FromQuery] int? limit,
        CancellationToken ct) =>
        servico.ListarAuditoriaAsync(new FiltroAuditoria(acao, entidade, entidadeId, unidadeId, de, ate, page, limit), ct);
}
