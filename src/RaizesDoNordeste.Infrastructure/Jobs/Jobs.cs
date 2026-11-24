using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaizesDoNordeste.Application.Pedidos;

namespace RaizesDoNordeste.Infrastructure.Jobs;

public sealed class JobsOpcoes
{
    public const string Secao = "Jobs";

    /// <summary>Permite desligar os jobs (ex.: em testes, ou numa instância dedicada só à API).</summary>
    public bool Habilitados { get; set; } = true;
    public int IntervaloOutboxSegundos { get; set; } = 5;
    public int IntervaloExpiracaoSegundos { get; set; } = 60;
}

/// <summary>Executa periodicamente a expiração de pedidos não pagos.</summary>
internal sealed class ExpiracaoPedidosJob(IServiceScopeFactory scopes, IOptions<JobsOpcoes> opcoes, TimeProvider relogio, ILogger<ExpiracaoPedidosJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(opcoes.Value.IntervaloExpiracaoSegundos);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var servico = scope.ServiceProvider.GetRequiredService<ExpiracaoPedidosService>();
                await servico.ExpirarVencidosAsync(lote: 100, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha no job de expiração de pedidos");
            }

            try { await Task.Delay(intervalo, relogio, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
