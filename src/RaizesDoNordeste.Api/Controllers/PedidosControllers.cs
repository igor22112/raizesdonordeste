using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RaizesDoNordeste.Api.Infra;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Pagamentos;
using RaizesDoNordeste.Application.Pedidos;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.Infrastructure.Pagamentos;

namespace RaizesDoNordeste.Api.Controllers;

[ApiController]
[Route("api/v1/pedidos")]
[Authorize]
[Produces("application/json")]
[Tags("Pedidos")]
public sealed class PedidosController(PedidoService pedidos, PagamentoService pagamentos) : ControllerBase
{
    /// <summary>
    /// Cria um pedido. O campo canalPedido é obrigatório (App, Web, Totem, Balcao, Pickup) e deve ser compatível com o perfil.
    /// O header Idempotency-Key é obrigatório: reenviar a mesma chave devolve o pedido já criado (200) em vez de duplicar.
    /// O estoque fica reservado até o pagamento ou a expiração do prazo.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Perfis.QuemFazPedido)]
    [ProducesResponseType<PedidoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<PedidoDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PedidoDto>> Criar(
        CriarPedidoRequest req, [FromHeader(Name = "Idempotency-Key")] string? chaveIdempotencia, CancellationToken ct)
    {
        var r = await pedidos.CriarAsync(req, chaveIdempotencia, ct);
        return r.Criado ? CreatedAtAction(nameof(Obter), new { id = r.Pedido.Id }, r.Pedido) : Ok(r.Pedido);
    }

    /// <summary>
    /// Lista pedidos visíveis ao usuário (cliente: os seus; equipe: os da unidade; matriz: todos), paginados.
    /// Filtros: canalPedido, status, unidadeId, período. Ex.: ?canalPedido=Totem&amp;status=AguardandoPagamento&amp;page=1&amp;limit=10
    /// </summary>
    [HttpGet]
    public Task<ResultadoPaginado<PedidoResumoDto>> Listar(
        [FromQuery] Guid? unidadeId, [FromQuery] StatusPedido? status, [FromQuery] CanalPedido? canalPedido,
        [FromQuery] DateTimeOffset? de, [FromQuery] DateTimeOffset? ate,
        [FromQuery] int? page, [FromQuery] int? limit, CancellationToken ct) =>
        pedidos.ListarAsync(new FiltroPedidos(unidadeId, status, canalPedido, de, ate, page, limit), ct);

    /// <summary>Detalhe e acompanhamento do pedido (status, histórico e pagamentos).</summary>
    [HttpGet("{id:guid}")]
    public Task<PedidoDto> Obter(Guid id, CancellationToken ct) => pedidos.ObterAsync(id, ct);

    /// <summary>Fluxo de preparo: EmPreparo → Pronto → Entregue (equipe da unidade). Na entrega, o cliente ganha pontos.</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = Perfis.EquipeLoja)]
    public Task<PedidoDto> AtualizarStatus(Guid id, AtualizarStatusPedidoRequest req, CancellationToken ct) =>
        pedidos.AtualizarStatusAsync(id, req, ct);

    /// <summary>Cancela com motivo (auditado). Em preparo, só a gerência. Se já pago, solicita estorno ao gateway.</summary>
    [HttpPost("{id:guid}/cancelamento")]
    public Task<PedidoDto> Cancelar(Guid id, CancelarPedidoRequest req, CancellationToken ct) => pedidos.CancelarAsync(id, req, ct);

    /// <summary>
    /// Solicita o pagamento ao gateway externo. Responde 202: a confirmação chega depois via webhook
    /// e o status do pedido muda para Pago. Se o gateway estiver fora, responde 503 com Retry-After.
    /// </summary>
    [HttpPost("{id:guid}/pagamentos")]
    [Authorize(Roles = Perfis.QuemFazPedido)]
    [ProducesResponseType<PagamentoDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PagamentoDto>> Pagar(Guid id, SolicitarPagamentoRequest req, CancellationToken ct)
    {
        var p = await pagamentos.SolicitarAsync(id, req, ct);
        return AcceptedAtAction(nameof(Obter), new { id }, p);
    }
}

[ApiController]
[Route("api/v1/webhooks/pagamentos")]
[AllowAnonymous]
[Tags("Integrações")]
public sealed class WebhookPagamentosController(PagamentoService pagamentos, IOptions<GatewayOpcoes> opcoes, TimeProvider relogio, ILogger<WebhookPagamentosController> logger)
    : ControllerBase
{
    public const string HeaderAssinatura = "X-Assinatura";
    public const string HeaderTimestamp = "X-Timestamp";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// Recebe a confirmação/negativa do gateway. A requisição só é aceita com assinatura HMAC-SHA256
    /// válida (headers X-Assinatura e X-Timestamp, tolerância de 5 minutos). Idempotente.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        using var leitor = new StreamReader(Request.Body);
        var corpo = await leitor.ReadToEndAsync(ct);

        var valido = AssinaturaWebhook.Validar(opcoes.Value.SegredoWebhook,
            Request.Headers[HeaderTimestamp].FirstOrDefault(), Request.Headers[HeaderAssinatura].FirstOrDefault(),
            corpo, relogio.GetUtcNow(), TimeSpan.FromMinutes(5));
        if (!valido)
        {
            logger.LogWarning("Webhook de pagamento com assinatura inválida rejeitado (IP {Ip}).", HttpContext.Connection.RemoteIpAddress);
            throw new NaoAutenticadoException("Assinatura do webhook inválida.");
        }

        NotificacaoPagamento? notificacao;
        try { notificacao = JsonSerializer.Deserialize<NotificacaoPagamento>(corpo, Json); }
        catch (JsonException) { notificacao = null; }
        if (notificacao is null) throw new FluentValidation.ValidationException("Corpo do webhook inválido.");

        await pagamentos.ProcessarNotificacaoAsync(notificacao, ct);
        return Ok(new { recebido = true });
    }
}
