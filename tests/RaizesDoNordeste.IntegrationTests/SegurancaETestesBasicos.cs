using System.Net;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Identidade;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.IntegrationTests.Infra;

namespace RaizesDoNordeste.IntegrationTests;

[Collection(ColecaoApi.Nome)]
public class AutenticacaoTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    [Fact]
    public async Task CT01_Registro_e_login_de_cliente()
    {
        var cliente = await _c.CriarClienteAsync();
        var eu = await cliente.Http.GetOk<UsuarioDto>("/api/v1/auth/me");
        eu.Email.ShouldBe(cliente.Email);
        eu.Perfil.ShouldBe(Perfil.Cliente);
    }

    [Fact]
    public async Task CT02_Registro_sem_aceite_dos_termos_retorna_400()
    {
        var r = await _c.Anonimo().Post("/api/v1/auth/registro",
            new RegistrarClienteRequest("Fulano", $"f-{Guid.NewGuid():N}@teste.local", "Senha@123", null, null, false, false, false));
        await r.DeveFalhar(HttpStatusCode.BadRequest, "VALIDACAO");
    }

    [Fact]
    public async Task CT03_Email_duplicado_retorna_409()
    {
        var cliente = await _c.CriarClienteAsync();
        var r = await _c.Anonimo().Post("/api/v1/auth/registro",
            new RegistrarClienteRequest("Outro", cliente.Email.ToUpperInvariant(), "Senha@123", null, null, true, false, false));
        await r.DeveFalhar(HttpStatusCode.Conflict, "EMAIL_EM_USO");
    }

    [Fact]
    public async Task CT04_Senha_errada_retorna_401_sem_revelar_se_o_email_existe()
    {
        var cliente = await _c.CriarClienteAsync();
        var senhaErrada = await _c.Anonimo().Post("/api/v1/auth/login", new LoginRequest(cliente.Email, "Errada@123"));
        var inexistente = await _c.Anonimo().Post("/api/v1/auth/login", new LoginRequest("ninguem@teste.local", "Errada@123"));
        await senhaErrada.DeveFalhar(HttpStatusCode.Unauthorized, "NAO_AUTENTICADO");
        await inexistente.DeveFalhar(HttpStatusCode.Unauthorized, "NAO_AUTENTICADO");
        (await senhaErrada.Content.ReadAsStringAsync()).ShouldNotContain("senha");
    }

    [Fact]
    public async Task CT05_Sem_token_401_e_perfil_errado_403()
    {
        var semToken = await _c.Anonimo().GetAsync("/api/v1/pedidos");
        await semToken.DeveFalhar(HttpStatusCode.Unauthorized, "NAO_AUTENTICADO");
        (await semToken.Content.ReadAsStringAsync()).ShouldContain("\"timestamp\"");
        var cliente = await _c.CriarClienteAsync();
        await (await cliente.Http.GetAsync("/api/v1/auditoria")).DeveFalhar(HttpStatusCode.Forbidden, "ACESSO_NEGADO");
        (await cliente.Http.Post("/api/v1/produtos", new { nome = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CT06_Token_adulterado_e_rejeitado()
    {
        var http = _c.Anonimo();
        http.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4Iiwicm9sZSI6IkFkbWluTWF0cml6In0.invalida");
        (await http.GetAsync("/api/v1/auditoria")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CT07_Login_limitado_por_IP_retorna_429()
    {
        await using var api = fixture.CriarApi(b => b.UseSetting("RateLimit:LoginPorMinuto", "3"));
        var http = api.CreateClient();
        var respostas = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            respostas.Add((await http.Post("/api/v1/auth/login", new LoginRequest("x@teste.local", "Errada@123"))).StatusCode);
        respostas.Take(3).ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
        respostas.Skip(3).ShouldAllBe(s => s == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task CT08_Senha_armazenada_com_hash()
    {
        var cliente = await _c.CriarClienteAsync();
        var hash = await _c.NoBancoAsync(db => db.Usuarios.Where(u => u.Id == cliente.Id).Select(u => u.SenhaHash).FirstAsync());
        hash.ShouldNotContain(Cenario.SenhaPadrao);
        hash.Length.ShouldBeGreaterThan(40);
    }
}

[Collection(ColecaoApi.Nome)]
public class InfraestruturaTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    [Fact]
    public async Task CT09_Health_checks_respondem()
    {
        (await _c.Anonimo().GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _c.Anonimo().GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CT10_Correlation_id_e_devolvido_e_erros_seguem_problem_details()
    {
        var http = _c.Anonimo();
        http.DefaultRequestHeaders.Add("X-Correlation-Id", "teste-correlacao-123");
        var r = await http.GetAsync($"/api/v1/unidades/{Guid.NewGuid()}");
        r.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("teste-correlacao-123");
        r.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        await r.DeveFalhar(HttpStatusCode.NotFound, "NAO_ENCONTRADO");
    }

    [Fact]
    public async Task CT11_Auditoria_e_imutavel_no_banco()
    {
        var loja = await _c.CriarLojaAsync();
        (await loja.Gerente.Post($"/api/v1/unidades/{loja.Unidade.Id}/estoque/movimentacoes",
            new { produtoId = loja.Produtos[0].Id, tipo = "Perda", quantidade = 1, motivo = "Queda no chão" })).EnsureSuccessStatusCode();

        await Should.ThrowAsync<InvalidOperationException>(() => _c.NoBancoAsync(async db =>
        {
            var registro = await db.Auditoria.OrderByDescending(a => a.OcorridoEm).FirstAsync();
            db.Entry(registro).Property(nameof(registro.Acao)).CurrentValue = "Adulterado";
            return await db.SaveChangesAsync();
        }));
    }
}
