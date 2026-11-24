using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RaizesDoNordeste.Application.Catalogo;
using RaizesDoNordeste.Application.Identidade;
using RaizesDoNordeste.Application.Pedidos;
using RaizesDoNordeste.Application.Unidades;
using RaizesDoNordeste.Domain.Catalogo;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.Domain.Unidades;
using RaizesDoNordeste.Infrastructure.Persistencia;
using System.Runtime.CompilerServices;

namespace RaizesDoNordeste.IntegrationTests.Infra;

public static class HttpExt
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static Task<HttpResponseMessage> Post(this HttpClient c, string url, object? corpo = null, string? chaveIdempotencia = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo ?? new { }, options: Json) };
        if (chaveIdempotencia is not null) req.Headers.Add("Idempotency-Key", chaveIdempotencia);
        return c.SendAsync(req);
    }

    public static Task<HttpResponseMessage> Put(this HttpClient c, string url, object corpo) => c.PutAsJsonAsync(url, corpo, Json);

    public static Task<HttpResponseMessage> Patch(this HttpClient c, string url, object corpo) => c.PatchAsJsonAsync(url, corpo, Json);

    public static async Task<T> Ler<T>(this HttpResponseMessage r, HttpStatusCode? esperado = null)
    {
        if (esperado is { } e && r.StatusCode != e)
            throw new Xunit.Sdk.XunitException($"Esperado HTTP {(int)e}, obtido {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        if (esperado is null && !r.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"Falha HTTP {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task<T> GetOk<T>(this HttpClient c, string url) => await (await c.GetAsync(url)).Ler<T>();

    /// <summary>Valida status HTTP e o "codigo" do ProblemDetails.</summary>
    public static async Task DeveFalhar(this HttpResponseMessage r, HttpStatusCode status, string? codigo = null)
    {
        var corpo = await r.Content.ReadAsStringAsync();
        ((int)r.StatusCode).ShouldBe((int)status, corpo);
        if (codigo is not null)
        {
            using var doc = JsonDocument.Parse(corpo);
            doc.RootElement.GetProperty("codigo").GetString().ShouldBe(codigo, corpo);
        }
    }
}

public sealed record Loja(UnidadeDto Unidade, IReadOnlyList<ProdutoDto> Produtos, HttpClient Gerente, HttpClient Atendente, HttpClient Cozinha, HttpClient Totem);

public sealed record ClienteTeste(HttpClient Http, Guid Id, string Email);

/// <summary>Monta dados de teste via API (como um usuário real faria). Cada teste cria a própria loja, isolando os dados.</summary>
public sealed class Cenario(ApiFixture fixture, WebApplicationFactory<Program>? api = null)
{
    public const string SenhaPadrao = "Senha@123";
    public WebApplicationFactory<Program> Api { get; } = api ?? fixture.Api;
    public ApiFixture Fixture { get; } = fixture;

    private HttpClient? _admin;

    public HttpClient Anonimo() => Api.CreateClient();

    public async Task<HttpClient> AdminAsync() => _admin ??= await LoginAsync(ApiFixture.AdminEmail, ApiFixture.AdminSenha);

    public async Task<HttpClient> LoginAsync(string email, string senha)
    {
        var token = await Anonimo().Post("/api/v1/auth/login", new LoginRequest(email, senha)).Result.Ler<TokenResponse>();
        var http = Api.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        CanaisPorCliente.AddOrUpdate(http, new CanalPadrao(token.Usuario.Perfil switch
        {
            Perfil.Totem => CanalPedido.Totem,
            Perfil.Atendente or Perfil.Gerente or Perfil.Cozinha => CanalPedido.Balcao,
            _ => CanalPedido.App
        }));
        return http;
    }

    public async Task<UnidadeDto> CriarUnidadeAsync(TipoUnidade tipo = TipoUnidade.CozinhaCompleta, TimeOnly? abre = null, TimeOnly? fecha = null,
        Regiao regiao = Regiao.Nordeste, string fuso = "America/Recife")
    {
        var admin = await AdminAsync();
        var req = new UnidadeRequest($"Unidade {Guid.NewGuid():N}"[..20], "Recife", "PE", regiao, tipo, fuso, abre ?? new(0, 0), fecha ?? new(0, 0));
        return await (await admin.Post("/api/v1/unidades", req)).Ler<UnidadeDto>(HttpStatusCode.Created);
    }

    public async Task<ProdutoDto> CriarProdutoAsync(decimal preco, bool requerCozinhaCompleta = false, string? sazonalInicio = null, string? sazonalFim = null)
    {
        var admin = await AdminAsync();
        var req = new ProdutoRequest($"Produto {Guid.NewGuid():N}"[..20], null, CategoriaProduto.Tapioca, preco, requerCozinhaCompleta, sazonalInicio, sazonalFim);
        return await (await admin.Post("/api/v1/produtos", req)).Ler<ProdutoDto>(HttpStatusCode.Created);
    }

    public async Task LiberarNoCardapioAsync(Guid unidadeId, Guid produtoId, decimal? precoLocal = null)
    {
        var admin = await AdminAsync();
        (await admin.Put($"/api/v1/unidades/{unidadeId}/cardapio/{produtoId}", new ConfigurarItemCardapioRequest(true, precoLocal, null))).EnsureSuccessStatusCode();
    }

    public async Task EntradaEstoqueAsync(Guid unidadeId, Guid produtoId, int quantidade)
    {
        var admin = await AdminAsync();
        (await admin.Post($"/api/v1/unidades/{unidadeId}/estoque/movimentacoes",
            new { produtoId, tipo = "Entrada", quantidade, motivo = "Carga de teste" })).EnsureSuccessStatusCode();
    }

    public async Task<HttpClient> CriarEquipeAsync(Perfil perfil, Guid unidadeId)
    {
        var admin = await AdminAsync();
        var email = $"{perfil}-{Guid.NewGuid():N}@teste.local".ToLowerInvariant();
        (await admin.Post("/api/v1/usuarios", new CriarUsuarioInternoRequest($"{perfil} teste", email, SenhaPadrao, perfil, unidadeId)))
            .EnsureSuccessStatusCode();
        return await LoginAsync(email, SenhaPadrao);
    }

    public async Task<ClienteTeste> CriarClienteAsync(bool fidelidade = true, bool campanhas = true, DateOnly? nascimento = null)
    {
        var email = $"cliente-{Guid.NewGuid():N}@teste.local";
        var u = await (await Anonimo().Post("/api/v1/auth/registro",
            new RegistrarClienteRequest("Cliente Teste", email, SenhaPadrao, nascimento, null, true, fidelidade, campanhas))).Ler<UsuarioDto>(HttpStatusCode.Created);
        return new ClienteTeste(await LoginAsync(email, SenhaPadrao), u.Id, email);
    }

    /// <summary>Unidade 24h com produtos liberados e estoque, mais um usuário de cada perfil da loja.</summary>
    public async Task<Loja> CriarLojaAsync(int estoque = 100, TipoUnidade tipo = TipoUnidade.CozinhaCompleta, Regiao regiao = Regiao.Nordeste, params decimal[] precos)
    {
        if (precos.Length == 0) precos = [10m, 20m];
        var unidade = await CriarUnidadeAsync(tipo, regiao: regiao);
        var produtos = new List<ProdutoDto>();
        foreach (var preco in precos)
        {
            var p = await CriarProdutoAsync(preco);
            await LiberarNoCardapioAsync(unidade.Id, p.Id);
            await EntradaEstoqueAsync(unidade.Id, p.Id, estoque);
            produtos.Add(p);
        }
        return new Loja(unidade, produtos,
            await CriarEquipeAsync(Perfil.Gerente, unidade.Id),
            await CriarEquipeAsync(Perfil.Atendente, unidade.Id),
            await CriarEquipeAsync(Perfil.Cozinha, unidade.Id),
            await CriarEquipeAsync(Perfil.Totem, unidade.Id));
    }

    private sealed record CanalPadrao(CanalPedido Canal);
    private static readonly ConditionalWeakTable<HttpClient, CanalPadrao> CanaisPorCliente = new();

    /// <summary>Canal coerente com o perfil do usuário logado (Cliente → App, Totem → Totem, equipe → Balcao).</summary>
    public static CanalPedido CanalDe(HttpClient http) => CanaisPorCliente.TryGetValue(http, out var c) ? c.Canal : CanalPedido.App;

    public static Task<HttpResponseMessage> CriarPedido(HttpClient http, Guid unidadeId, Guid produtoId, int quantidade = 1, int pontos = 0,
        Guid? campanhaId = null, string? chave = null, CanalPedido? canal = null) =>
        http.Post("/api/v1/pedidos", new CriarPedidoRequest(unidadeId, [new ItemPedidoRequest(produtoId, quantidade)], canal ?? CanalDe(http), pontos, campanhaId),
            chave ?? Guid.NewGuid().ToString());

    public static async Task<PedidoDto> AguardarStatusAsync(HttpClient http, Guid pedidoId, StatusPedido status, int timeoutMs = 10_000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        PedidoDto pedido;
        do
        {
            pedido = await http.GetOk<PedidoDto>($"/api/v1/pedidos/{pedidoId}");
            if (pedido.Status == status) return pedido;
            await Task.Delay(100);
        } while (DateTime.UtcNow < limite);
        throw new Xunit.Sdk.XunitException($"Pedido {pedidoId} não chegou a {status} (atual: {pedido.Status}).");
    }

    public static async Task<PedidoDto> AguardarPagamentoAsync(HttpClient http, Guid pedidoId, Domain.Pagamentos.StatusPagamento status, int timeoutMs = 10_000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        PedidoDto pedido;
        do
        {
            pedido = await http.GetOk<PedidoDto>($"/api/v1/pedidos/{pedidoId}");
            if (pedido.Pagamentos.Count > 0 && pedido.Pagamentos[^1].Status == status) return pedido;
            await Task.Delay(100);
        } while (DateTime.UtcNow < limite);
        throw new Xunit.Sdk.XunitException($"Pagamento do pedido {pedidoId} não chegou a {status}.");
    }

    /// <summary>Cria, paga e espera a confirmação do gateway.</summary>
    public static async Task<PedidoDto> CriarPedidoPagoAsync(HttpClient http, Guid unidadeId, Guid produtoId, int quantidade = 1, int pontos = 0, Guid? campanhaId = null)
    {
        var pedido = await (await CriarPedido(http, unidadeId, produtoId, quantidade, pontos, campanhaId)).Ler<PedidoDto>(HttpStatusCode.Created);
        (await http.Post($"/api/v1/pedidos/{pedido.Id}/pagamentos", new { metodo = "Pix" })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return await AguardarStatusAsync(http, pedido.Id, StatusPedido.Pago);
    }

    public async Task<T> NoBancoAsync<T>(Func<AppDbContext, Task<T>> consulta)
    {
        using var scope = Api.Services.CreateScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
