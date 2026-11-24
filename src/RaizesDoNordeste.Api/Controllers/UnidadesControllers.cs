using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using RaizesDoNordeste.Api.Infra;
using RaizesDoNordeste.Application.Catalogo;
using RaizesDoNordeste.Application.Estoques;
using RaizesDoNordeste.Application.Unidades;
using RaizesDoNordeste.Domain.Catalogo;
using RaizesDoNordeste.Domain.Unidades;

namespace RaizesDoNordeste.Api.Controllers;

[ApiController]
[Route("api/v1/unidades")]
[Produces("application/json")]
[Tags("Unidades")]
public sealed class UnidadesController(UnidadeService servico) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<UnidadeDto>> Listar([FromQuery] Regiao? regiao, CancellationToken ct) => servico.ListarAsync(regiao, ct);

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public Task<UnidadeDto> Obter(Guid id, CancellationToken ct) => servico.ObterAsync(id, ct);

    [HttpPost]
    [Authorize(Roles = Perfis.Admin)]
    [ProducesResponseType<UnidadeDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UnidadeDto>> Criar(UnidadeRequest req, CancellationToken ct)
    {
        var u = await servico.CriarAsync(req, ct);
        return CreatedAtAction(nameof(Obter), new { id = u.Id }, u);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Perfis.Admin)]
    public Task<UnidadeDto> Atualizar(Guid id, UnidadeRequest req, CancellationToken ct) => servico.AtualizarAsync(id, req, ct);
}

[ApiController]
[Route("api/v1/unidades/{unidadeId:guid}/cardapio")]
[Produces("application/json")]
[Tags("Cardápio")]
public sealed class CardapioController(CardapioService servico) : ControllerBase
{
    /// <summary>
    /// Cardápio da unidade (público): só itens liberados, compatíveis com a cozinha e dentro da temporada.
    /// Resposta em cache por alguns segundos para aliviar o banco no horário de pico.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [OutputCache(PolicyName = "Cardapio")]
    public Task<CardapioDto> Obter(Guid unidadeId, CancellationToken ct) => servico.ObterAsync(unidadeId, ct);

    /// <summary>Gerência libera/oculta produto, define preço local e variação regional (alteração de preço é auditada).</summary>
    [HttpPut("{produtoId:guid}")]
    [Authorize(Roles = Perfis.GerenciaOuAdmin)]
    public Task<ItemCardapioDto> Configurar(Guid unidadeId, Guid produtoId, ConfigurarItemCardapioRequest req, CancellationToken ct) =>
        servico.ConfigurarItemAsync(unidadeId, produtoId, req, ct);
}

[ApiController]
[Route("api/v1/unidades/{unidadeId:guid}/estoque")]
[Produces("application/json")]
[Tags("Estoque")]
public sealed class EstoqueController(EstoqueService servico) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = Perfis.EquipeLoja + "," + Perfis.Admin)]
    public Task<IReadOnlyList<ItemEstoqueDto>> Listar(Guid unidadeId, [FromQuery] bool abaixoDoMinimo, CancellationToken ct) =>
        servico.ListarAsync(unidadeId, abaixoDoMinimo, ct);

    /// <summary>Entrada, ajuste de inventário ou perda. Ajustes e perdas exigem motivo e são auditados.</summary>
    [HttpPost("movimentacoes")]
    [Authorize(Roles = Perfis.GerenciaOuAdmin)]
    [ProducesResponseType<ItemEstoqueDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ItemEstoqueDto>> Movimentar(Guid unidadeId, MovimentarEstoqueRequest req, CancellationToken ct)
    {
        var item = await servico.MovimentarAsync(unidadeId, req, ct);
        return StatusCode(StatusCodes.Status201Created, item);
    }

    [HttpPut("{produtoId:guid}/minimo")]
    [Authorize(Roles = Perfis.GerenciaOuAdmin)]
    public Task<ItemEstoqueDto> DefinirMinimo(Guid unidadeId, Guid produtoId, DefinirEstoqueMinimoRequest req, CancellationToken ct) =>
        servico.DefinirMinimoAsync(unidadeId, produtoId, req, ct);
}

[ApiController]
[Route("api/v1/produtos")]
[Produces("application/json")]
[Tags("Catálogo")]
public sealed class ProdutosController(ProdutoService servico) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<ProdutoDto>> Listar([FromQuery] CategoriaProduto? categoria, CancellationToken ct) => servico.ListarAsync(categoria, ct);

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public Task<ProdutoDto> Obter(Guid id, CancellationToken ct) => servico.ObterAsync(id, ct);

    [HttpPost]
    [Authorize(Roles = Perfis.Admin)]
    [ProducesResponseType<ProdutoDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProdutoDto>> Criar(ProdutoRequest req, CancellationToken ct)
    {
        var p = await servico.CriarAsync(req, ct);
        return CreatedAtAction(nameof(Obter), new { id = p.Id }, p);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Perfis.Admin)]
    public Task<ProdutoDto> Atualizar(Guid id, ProdutoRequest req, CancellationToken ct) => servico.AtualizarAsync(id, req, ct);
}
