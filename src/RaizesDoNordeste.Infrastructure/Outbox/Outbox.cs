using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Infrastructure.Jobs;
using RaizesDoNordeste.Infrastructure.Persistencia;

namespace RaizesDoNordeste.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(string tipo, string payload, DateTimeOffset ocorridoEm)
    {
        Id = Guid.NewGuid();
        Tipo = tipo;
        Payload = payload;
        OcorridoEm = ocorridoEm;
    }

    public Guid Id { get; private set; }
    public string Tipo { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTimeOffset OcorridoEm { get; private set; }
    public DateTimeOffset? ProcessadoEm { get; private set; }
    public int Tentativas { get; private set; }
    public string? UltimoErro { get; private set; }

    public void MarcarProcessada(DateTimeOffset agora) => ProcessadoEm = agora;

    public void RegistrarFalha(string erro)
    {
        Tentativas++;
        UltimoErro = erro.Length > 1000 ? erro[..1000] : erro;
    }
}

/// <summary>
/// Publica as mensagens da Outbox (entrega "pelo menos uma vez"). Consumidores devem ser
/// idempotentes usando o Id da mensagem. Com várias instâncias da API, a evolução é usar
/// UPDATE ... WITH (READPAST) para que cada instância reivindique lotes diferentes.
/// </summary>
internal sealed class OutboxDispatcher(IServiceScopeFactory scopes, IOptions<JobsOpcoes> opcoes, TimeProvider relogio, ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    public const int MaxTentativas = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(opcoes.Value.IntervaloOutboxSegundos);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarLoteAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha ao processar a Outbox");
            }

            try { await Task.Delay(intervalo, relogio, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task<int> ProcessarLoteAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publicador = scope.ServiceProvider.GetRequiredService<IPublicadorEventos>();

        var mensagens = await db.Outbox
            .Where(m => m.ProcessadoEm == null && m.Tentativas < MaxTentativas)
            .OrderBy(m => m.OcorridoEm)
            .Take(50)
            .ToListAsync(ct);

        foreach (var m in mensagens)
        {
            try
            {
                await publicador.PublicarAsync(m.Tipo, m.Payload, ct);
                m.MarcarProcessada(relogio.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m.RegistrarFalha(ex.Message);
                logger.LogWarning(ex, "Falha ao publicar a mensagem {MensagemId} ({Tipo})", m.Id, m.Tipo);
            }
        }

        if (mensagens.Count > 0) await db.SaveChangesAsync(ct);
        return mensagens.Count;
    }
}

/// <summary>
/// Publicador que apenas registra em log. Em produção seria substituído por um broker
/// (Azure Service Bus, RabbitMQ...) consumido pelo painel da cozinha, notificações push e BI.
/// </summary>
internal sealed class PublicadorEventosLog(ILogger<PublicadorEventosLog> logger) : IPublicadorEventos
{
    public Task PublicarAsync(string tipo, string payloadJson, CancellationToken ct)
    {
        logger.LogInformation("Evento publicado {TipoEvento}: {Payload}", tipo, payloadJson);
        return Task.CompletedTask;
    }
}
