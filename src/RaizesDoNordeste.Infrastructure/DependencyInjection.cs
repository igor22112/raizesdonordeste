using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Infrastructure.Jobs;
using RaizesDoNordeste.Infrastructure.Outbox;
using RaizesDoNordeste.Infrastructure.Pagamentos;
using RaizesDoNordeste.Infrastructure.Persistencia;
using RaizesDoNordeste.Infrastructure.Seed;
using RaizesDoNordeste.Infrastructure.Seguranca;

namespace RaizesDoNordeste.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("ConnectionStrings:SqlServer não configurada.");

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString, sql =>
        {
            // Re-tenta automaticamente falhas transitórias do SQL Server (failover, deadlock, timeout de conexão).
            sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            sql.CommandTimeout(30);
        }));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<JwtOpcoes>().Bind(config.GetSection(JwtOpcoes.Secao)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<GatewayOpcoes>().Bind(config.GetSection(GatewayOpcoes.Secao)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<JobsOpcoes>().Bind(config.GetSection(JobsOpcoes.Secao));
        services.AddOptions<SeedOpcoes>().Bind(config.GetSection(SeedOpcoes.Secao));
        services.AddOptions<PedidoOpcoes>().Bind(config.GetSection(PedidoOpcoes.Secao));
        services.AddOptions<PrivacidadeOpcoes>().Bind(config.GetSection(PrivacidadeOpcoes.Secao));

        services.AddSingleton<ISenhaHasher, SenhaHasher>();
        services.AddSingleton<IGeradorToken, GeradorTokenJwt>();
        services.AddSingleton<IPublicadorEventos, PublicadorEventosLog>();
        services.AddScoped<DataSeeder>();

        services.AddHttpClient<IGatewayPagamento, GatewayPagamentoClient>((sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<GatewayOpcoes>>().Value;
                http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
                http.Timeout = Timeout.InfiniteTimeSpan; // quem controla o tempo é o pipeline de resiliência
            })
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions r, IServiceProvider sp) =>
            {
                var o = sp.GetRequiredService<IOptions<GatewayOpcoes>>().Value;
                r.AttemptTimeout.Timeout = TimeSpan.FromSeconds(o.TimeoutTentativaSegundos);
                r.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(o.TimeoutTotalSegundos);
                r.Retry.MaxRetryAttempts = Math.Max(1, o.MaxRetentativas);
                r.Retry.BackoffType = DelayBackoffType.Exponential;
                r.Retry.UseJitter = true;
                r.Retry.Delay = TimeSpan.FromMilliseconds(200);
                // POST é re-tentado de propósito: toda chamada leva Idempotency-Key, então o gateway não cobra duas vezes.
                r.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, o.TimeoutTentativaSegundos * 2 + 1));
                r.CircuitBreaker.FailureRatio = 0.5;
                r.CircuitBreaker.MinimumThroughput = 5;
                r.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });

        if (config.GetValue(JobsOpcoes.Secao + ":Habilitados", true))
        {
            services.AddHostedService<OutboxDispatcher>();
            services.AddHostedService<ExpiracaoPedidosJob>();
        }

        return services;
    }
}
