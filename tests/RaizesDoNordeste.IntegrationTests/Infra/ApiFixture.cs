extern alias Gateway;
using Gateway::PaymentGateway.Mock;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Testcontainers.MsSql;

namespace RaizesDoNordeste.IntegrationTests.Infra;

/// <summary>
/// Sobe um SQL Server real (Testcontainers), o gateway simulado e a API em memória.
/// A API chama o gateway pelo handler do TestServer e o gateway entrega o webhook na API
/// do mesmo jeito: o fluxo assíncrono de pagamento é exercitado de ponta a ponta.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@teste.local";
    public const string AdminSenha = "Admin@12345";
    public const string ApiKey = "api-key-de-teste";
    public const string SegredoWebhook = "segredo-webhook-de-teste";

    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public WebApplicationFactory<GatewayMockMarker> Gateway { get; private set; } = null!;
    public WebApplicationFactory<Program> Api { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "RaizesTestes" };
        ConnectionString = builder.ConnectionString;

        Gateway = new GatewayFactory(() => Api.Server.CreateHandler());
        // O gateway roda em Kestrel (TCP real): timeouts e re-tentativas se comportam como em produção,
        // o que o servidor em memória (TestServer) não reproduz fielmente.
        var porta = PortaLivre();
        Gateway.UseKestrel(porta);
        Gateway.StartServer();
        // Endereço explícito: com "localhost" o Windows pode tentar primeiro a outra família de IP e
        // levar ~2s até a recusa, o que estouraria o timeout curto usado nos testes.
        GatewayUrl = await EnderecoQueRespondeAsync(porta);
        Api = CriarApi();
        _ = Api.Server; // força a inicialização (migrações + admin) antes dos testes
    }

    /// <summary>
    /// Cria uma instância nova da API apontando para o mesmo banco. Usada em testes que alteram
    /// estado em memória (circuit breaker, rate limit) para não contaminar os demais.
    /// </summary>
    public WebApplicationFactory<Program> CriarApi(Action<IWebHostBuilder>? extra = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testes");
            b.UseSetting("ConnectionStrings:SqlServer", ConnectionString);
            b.UseSetting("Jwt:Chave", "chave-jwt-de-testes-com-mais-de-32-caracteres");
            b.UseSetting("GatewayPagamento:BaseUrl", GatewayUrl);
            b.UseSetting("GatewayPagamento:UrlCallback", "http://localhost/api/v1/webhooks/pagamentos");
            b.UseSetting("GatewayPagamento:ApiKey", ApiKey);
            b.UseSetting("GatewayPagamento:SegredoWebhook", SegredoWebhook);
            b.UseSetting("GatewayPagamento:TimeoutTentativaSegundos", "1");
            b.UseSetting("GatewayPagamento:TimeoutTotalSegundos", "8");
            b.UseSetting("Seed:AdminEmail", AdminEmail);
            b.UseSetting("Seed:AdminSenha", AdminSenha);
            b.UseSetting("Seed:DadosDemonstracao", "false");
            b.UseSetting("Jobs:Habilitados", "false");
            b.UseSetting("Cache:CardapioSegundos", "0");
            b.UseSetting("RateLimit:RequisicoesPorJanela", "100000");
            b.UseSetting("RateLimit:LoginPorMinuto", "100000");
            b.UseSetting("Serilog:MinimumLevel:Default", "Warning");
            extra?.Invoke(b);
        });

    public string GatewayUrl { get; private set; } = null!;

    public Repositorio TransacoesDoGateway => Gateway.Services.GetRequiredService<Repositorio>();

    private static async Task<string> EnderecoQueRespondeAsync(int porta)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        foreach (var host in new[] { "127.0.0.1", "[::1]" })
        {
            try
            {
                var url = $"http://{host}:{porta}/";
                (await http.GetAsync(url + "health")).EnsureSuccessStatusCode();
                return url;
            }
            catch (Exception) when (host != "[::1]") { }
        }
        throw new InvalidOperationException("Gateway de testes não respondeu.");
    }

    private static int PortaLivre()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var porta = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return porta;
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await Gateway.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

public sealed class GatewayFactory(Func<HttpMessageHandler> handlerDaApi) : WebApplicationFactory<GatewayMockMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseSetting("Gateway:ApiKey", ApiFixture.ApiKey);
        b.UseSetting("Gateway:SegredoWebhook", ApiFixture.SegredoWebhook);
        b.UseSetting("Gateway:AtrasoWebhookMs", "150");
        b.UseSetting("Gateway:AtrasoLentoMs", "2500");
        // O webhook do gateway é entregue na API em memória.
        b.ConfigureTestServices(s => s.Configure<HttpClientFactoryOptions>("webhook", o =>
            o.HttpMessageHandlerBuilderActions.Add(h => h.PrimaryHandler = new HandlerTardio(handlerDaApi))));
    }
}

/// <summary>Resolve o handler só no primeiro uso (API e gateway dependem um do outro).</summary>
internal sealed class HandlerTardio(Func<HttpMessageHandler> fabrica) : HttpMessageHandler
{
    private readonly Lazy<HttpMessageInvoker> _invoker = new(() => new HttpMessageInvoker(fabrica(), disposeHandler: false));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        _invoker.Value.SendAsync(request, ct);
}

[CollectionDefinition(Nome)]
public sealed class ColecaoApi : ICollectionFixture<ApiFixture>
{
    public const string Nome = "API";
}
