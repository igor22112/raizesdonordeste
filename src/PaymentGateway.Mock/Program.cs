using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaymentGateway.Mock;

// Gateway de pagamento SIMULADO. Representa o serviço externo especializado do §5 do estudo de caso.
// É um sistema separado (outro processo, outro contrato) e por isso não compartilha código com a API.
//
// Regras de simulação por VALOR EXATO (valores improváveis em pedidos reais, para não gerar falhas aleatórias):
//   R$ 13,13 -> recusado de forma assíncrona (webhook "recusado")
//   R$ 14,14 -> recusado de forma síncrona (HTTP 422)
//   R$ 99,99 -> gateway fora do ar (HTTP 503 em toda chamada)
//   R$ 77,77 -> primeira chamada demora mais que o timeout do cliente (testa re-tentativa + idempotência)
//   demais   -> aprovado de forma assíncrona (webhook "aprovado")
// Também é possível derrubar o gateway inteiro com POST /simulacao/fora-do-ar {"ativo": true}.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Repositorio>();
builder.Services.AddHttpClient("webhook", c => c.Timeout = TimeSpan.FromSeconds(10));
var app = builder.Build();

var apiKey = app.Configuration["Gateway:ApiKey"] ?? "gateway-dev-api-key";
var segredo = app.Configuration["Gateway:SegredoWebhook"] ?? "segredo-webhook-desenvolvimento";
var atrasoWebhook = TimeSpan.FromMilliseconds(app.Configuration.GetValue("Gateway:AtrasoWebhookMs", 800));
var atrasoLento = TimeSpan.FromMilliseconds(app.Configuration.GetValue("Gateway:AtrasoLentoMs", 5000));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/v1") || ctx.Request.Path.StartsWithSegments("/simulacao"))
    {
        if (ctx.Request.Headers.Authorization.ToString() != $"Bearer {apiKey}")
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { motivo = "API key inválida" });
            return;
        }
        var repo = ctx.RequestServices.GetRequiredService<Repositorio>();
        if (repo.ForaDoAr && ctx.Request.Path.StartsWithSegments("/v1"))
        {
            ctx.Response.StatusCode = 503;
            return;
        }
    }
    await next();
});

app.MapPost("/v1/pagamentos", async (HttpContext ctx, SolicitacaoPagamento s, Repositorio repo, IHttpClientFactory http, ILogger<Program> log) =>
{
    var chave = ctx.Request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(chave)) return Results.BadRequest(new { motivo = "Idempotency-Key obrigatório" });
    if (s.Valor <= 0) return Results.UnprocessableEntity(new { motivo = "Valor inválido" });

    if (s.Valor == 99.99m) return Results.StatusCode(503);
    if (s.Valor == 14.14m) return Results.UnprocessableEntity(new { motivo = "Cartão bloqueado (simulação)" });

    // Idempotência: a mesma chave sempre devolve a mesma transação, sem cobrar de novo.
    var (transacao, nova) = repo.ObterOuCriar(chave, s);
    if (!nova)
    {
        log.LogInformation("Requisição repetida para a chave {Chave}: devolvendo transação {Id}", chave, transacao.Id);
        return Results.Accepted($"/v1/pagamentos/{transacao.Id}", Resposta(transacao));
    }

    if (s.Valor == 77.77m) await Task.Delay(atrasoLento); // simula lentidão: o cliente desiste e re-tenta

    var aprovar = s.Valor != 13.13m;
    _ = Task.Run(async () =>
    {
        await Task.Delay(atrasoWebhook);
        transacao.Status = aprovar ? "aprovado" : "recusado";
        transacao.Motivo = aprovar ? null : "Saldo insuficiente (simulação)";
        await EnviarWebhookAsync(http, s.UrlCallback, transacao, segredo, log);
    });

    return Results.Accepted($"/v1/pagamentos/{transacao.Id}", Resposta(transacao));
});

app.MapPost("/v1/pagamentos/{id}/estornos", (string id, Repositorio repo) =>
{
    var t = repo.Obter(id);
    if (t is null) return Results.NotFound();
    lock (t)
    {
        if (t.Status == "estornado") return Results.Conflict(new { motivo = "Transação já estornada" });
        if (t.Status != "aprovado") return Results.UnprocessableEntity(new { motivo = $"Transação {t.Status} não pode ser estornada" });
        t.Status = "estornado";
    }
    return Results.Ok(Resposta(t));
});

// Liga/desliga indisponibilidade total para demonstrar o circuit breaker da API.
app.MapPost("/simulacao/fora-do-ar", (EstadoSimulacao e, Repositorio repo) =>
{
    repo.ForaDoAr = e.Ativo;
    return Results.Ok(new { foraDoAr = repo.ForaDoAr });
});

app.Run();

static object Resposta(Transacao t) => new { transacaoId = t.Id, status = t.Status, motivo = t.Motivo, valor = t.Valor, referencia = t.Referencia };

static async Task EnviarWebhookAsync(IHttpClientFactory fabrica, string url, Transacao t, string segredo, ILogger log)
{
    var corpo = JsonSerializer.Serialize(new { referencia = t.Referencia, transacaoId = t.Id, status = t.Status, motivo = t.Motivo });
    for (var tentativa = 1; tentativa <= 4; tentativa++)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var assinatura = "sha256=" + Convert.ToHexStringLower(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes($"{timestamp}.{corpo}")));
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
            req.Headers.Add("X-Timestamp", timestamp);
            req.Headers.Add("X-Assinatura", assinatura);
            using var resp = await fabrica.CreateClient("webhook").SendAsync(req);
            if (resp.IsSuccessStatusCode) return;
            log.LogWarning("Webhook {Url} respondeu {Status} (tentativa {Tentativa})", url, (int)resp.StatusCode, tentativa);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falha ao enviar webhook para {Url} (tentativa {Tentativa})", url, tentativa);
        }
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, tentativa)));
    }
    log.LogError("Webhook da transação {Id} não entregue após 4 tentativas", t.Id);
}

namespace PaymentGateway.Mock
{
    /// <summary>Tipo marcador usado pelos testes de integração (WebApplicationFactory).</summary>
    public sealed class GatewayMockMarker;

    public sealed record SolicitacaoPagamento(Guid Referencia, Guid PedidoId, decimal Valor, string Metodo, string UrlCallback);

    public sealed record EstadoSimulacao(bool Ativo);

    public sealed class Transacao
    {
        public required string Id { get; init; }
        public required Guid Referencia { get; init; }
        public required decimal Valor { get; init; }
        public string Status { get; set; } = "processando";
        public string? Motivo { get; set; }
    }

    public sealed class Repositorio
    {
        private readonly ConcurrentDictionary<string, Transacao> _porChave = new();
        private readonly ConcurrentDictionary<string, Transacao> _porId = new();

        public bool ForaDoAr { get; set; }

        public (Transacao, bool nova) ObterOuCriar(string chave, SolicitacaoPagamento s)
        {
            var nova = false;
            var t = _porChave.GetOrAdd(chave, _ =>
            {
                nova = true;
                return new Transacao { Id = "tx_" + Guid.NewGuid().ToString("N")[..16], Referencia = s.Referencia, Valor = s.Valor };
            });
            _porId.TryAdd(t.Id, t);
            return (t, nova);
        }

        public Transacao? Obter(string id) => _porId.GetValueOrDefault(id);

        public IReadOnlyList<Transacao> PorReferencia(Guid referencia) => _porId.Values.Where(t => t.Referencia == referencia).ToList();

        /// <summary>Registra uma transação já aprovada (usado em testes de cenários de corrida).</summary>
        public Transacao RegistrarAprovada(Guid referencia, decimal valor)
        {
            var t = new Transacao { Id = "tx_" + Guid.NewGuid().ToString("N")[..16], Referencia = referencia, Valor = valor, Status = "aprovado" };
            _porId[t.Id] = t;
            return t;
        }
    }
}
