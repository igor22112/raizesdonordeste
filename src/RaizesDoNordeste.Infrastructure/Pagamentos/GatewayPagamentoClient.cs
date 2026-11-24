using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using RaizesDoNordeste.Application.Abstracoes;

namespace RaizesDoNordeste.Infrastructure.Pagamentos;

public sealed class GatewayOpcoes
{
    public const string Secao = "GatewayPagamento";

    [Required, Url] public string BaseUrl { get; set; } = "http://localhost:5100/";
    [Required] public string ApiKey { get; set; } = "";

    /// <summary>URL pública do nosso webhook, informada ao gateway em cada solicitação.</summary>
    [Required, Url] public string UrlCallback { get; set; } = "http://localhost:5000/api/v1/webhooks/pagamentos";

    /// <summary>Segredo compartilhado para validar a assinatura HMAC do webhook.</summary>
    [Required, MinLength(16)] public string SegredoWebhook { get; set; } = "";

    [Range(1, 30)] public int TimeoutTentativaSegundos { get; set; } = 3;
    [Range(1, 120)] public int TimeoutTotalSegundos { get; set; } = 10;
    [Range(0, 5)] public int MaxRetentativas { get; set; } = 2;
}

/// <summary>
/// Cliente HTTP do gateway. A resiliência (timeout por tentativa, re-tentativa com backoff
/// exponencial e jitter, circuit breaker e timeout total) é configurada no pipeline do
/// HttpClient (AddStandardResilienceHandler). Aqui convertemos as falhas para o contrato da aplicação.
/// </summary>
internal sealed class GatewayPagamentoClient(HttpClient http, IOptions<GatewayOpcoes> opcoes, ILogger<GatewayPagamentoClient> logger)
    : IGatewayPagamento
{
    private sealed record SolicitacaoBody(Guid Referencia, Guid PedidoId, decimal Valor, string Metodo, string UrlCallback);
    private sealed record TransacaoBody(string TransacaoId, string Status, string? Motivo);
    private sealed record ErroBody(string? Motivo);

    public async Task<RespostaSolicitacaoPagamento> SolicitarAsync(SolicitacaoPagamento s, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/pagamentos")
        {
            Content = JsonContent.Create(new SolicitacaoBody(s.PagamentoId, s.PedidoId, s.Valor, s.Metodo.ToString(), opcoes.Value.UrlCallback))
        };
        req.Headers.Add("Idempotency-Key", s.PagamentoId.ToString());

        using var resp = await EnviarAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var erro = await resp.Content.ReadFromJsonAsync<ErroBody>(ct);
            return new RespostaSolicitacaoPagamento(false, null, erro?.Motivo ?? "Pagamento recusado pelo gateway.");
        }
        GarantirSucesso(resp);
        var corpo = await resp.Content.ReadFromJsonAsync<TransacaoBody>(ct)
            ?? throw new GatewayIndisponivelException("Resposta vazia do gateway.");
        return new RespostaSolicitacaoPagamento(true, corpo.TransacaoId, null);
    }

    public async Task EstornarAsync(string transacaoId, Guid chaveIdempotencia, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/pagamentos/{Uri.EscapeDataString(transacaoId)}/estornos");
        req.Headers.Add("Idempotency-Key", $"estorno-{chaveIdempotencia}");
        using var resp = await EnviarAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict) return; // já estornado
        GarantirSucesso(resp);
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(req, ct);
        }
        catch (BrokenCircuitException ex)
        {
            logger.LogWarning("Circuito do gateway de pagamento aberto; chamada rejeitada sem tentar.");
            throw new GatewayIndisponivelException("Gateway de pagamento indisponível (circuito aberto).", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new GatewayIndisponivelException("Gateway de pagamento não respondeu a tempo.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new GatewayIndisponivelException("Falha de comunicação com o gateway de pagamento.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new GatewayIndisponivelException("Gateway de pagamento não respondeu a tempo.", ex);
        }
    }

    private static void GarantirSucesso(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        throw new GatewayIndisponivelException($"Gateway respondeu {(int)resp.StatusCode}.");
    }
}
