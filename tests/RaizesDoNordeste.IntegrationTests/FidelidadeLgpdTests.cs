using System.Net;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Campanhas;
using RaizesDoNordeste.Application.Fidelidade;
using RaizesDoNordeste.Application.Identidade;
using RaizesDoNordeste.Application.Pedidos;
using RaizesDoNordeste.Application.Privacidade;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.Domain.Unidades;
using RaizesDoNordeste.IntegrationTests.Infra;

namespace RaizesDoNordeste.IntegrationTests;

[Collection(ColecaoApi.Nome)]
public class FidelidadeTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    private static async Task Entregar(Loja loja, Guid pedidoId)
    {
        (await loja.Cozinha.Patch($"/api/v1/pedidos/{pedidoId}/status", new AtualizarStatusPedidoRequest(StatusPedido.EmPreparo))).EnsureSuccessStatusCode();
        (await loja.Cozinha.Patch($"/api/v1/pedidos/{pedidoId}/status", new AtualizarStatusPedidoRequest(StatusPedido.Pronto))).EnsureSuccessStatusCode();
        (await loja.Atendente.Patch($"/api/v1/pedidos/{pedidoId}/status", new AtualizarStatusPedidoRequest(StatusPedido.Entregue))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CT40_Pontos_ganhos_resgatados_e_devolvidos_no_cancelamento()
    {
        var loja = await _c.CriarLojaAsync(estoque: 50, precos: 50m);
        var cliente = await _c.CriarClienteAsync();
        var p1 = await Cenario.CriarPedidoPagoAsync(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, 2); // R$ 100
        await Entregar(loja, p1.Id);
        (await cliente.Http.GetOk<FidelidadeDto>("/api/v1/clientes/me/fidelidade")).SaldoPontos.ShouldBe(100);

        var p2 = await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, 1, pontos: 100)).Ler<PedidoDto>(HttpStatusCode.Created);
        p2.DescontoPontos.ShouldBe(5m);
        p2.Total.ShouldBe(45m);
        (await cliente.Http.GetOk<FidelidadeDto>("/api/v1/clientes/me/fidelidade")).SaldoPontos.ShouldBe(0);

        (await cliente.Http.Post($"/api/v1/pedidos/{p2.Id}/cancelamento", new CancelarPedidoRequest("Desisti"))).EnsureSuccessStatusCode();
        var fid = await cliente.Http.GetOk<FidelidadeDto>("/api/v1/clientes/me/fidelidade");
        fid.SaldoPontos.ShouldBe(100);
        fid.UltimasMovimentacoes.Select(m => m.Tipo).ShouldBe([TipoMovimentacaoPontos.Estorno, TipoMovimentacaoPontos.Debito, TipoMovimentacaoPontos.Credito]);
        (await _c.NoBancoAsync(db => db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.DescontoAplicado && a.EntidadeId == p2.Id.ToString()))).ShouldBeTrue();
    }

    [Fact]
    public async Task CT41_Desconto_progressivo_ao_atingir_nivel_prata()
    {
        var loja = await _c.CriarLojaAsync(estoque: 50, precos: 10m);
        var cliente = await _c.CriarClienteAsync();
        for (var i = 0; i < 5; i++)
        {
            var p = await Cenario.CriarPedidoPagoAsync(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id);
            await Entregar(loja, p.Id);
        }
        var fid = await cliente.Http.GetOk<FidelidadeDto>("/api/v1/clientes/me/fidelidade");
        fid.Nivel.ShouldBe(NivelFidelidade.Prata);

        var novo = await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, 10)).Ler<PedidoDto>(HttpStatusCode.Created);
        novo.DescontoNivel.ShouldBe(3m); // 3% de R$ 100
        novo.Total.ShouldBe(97m);
    }

    [Fact]
    public async Task CT42_Sem_consentimento_nao_ganha_nem_resgata_pontos()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var cliente = await _c.CriarClienteAsync(fidelidade: false);
        var p = await Cenario.CriarPedidoPagoAsync(cliente.Http, loja.Unidade.Id, loja.Produtos[1].Id);
        await Entregar(loja, p.Id);

        var fid = await cliente.Http.GetOk<FidelidadeDto>("/api/v1/clientes/me/fidelidade");
        fid.ParticipaDoPrograma.ShouldBeFalse();
        fid.SaldoPontos.ShouldBe(0);
        await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, 1, pontos: 10))
            .DeveFalhar(HttpStatusCode.Conflict, "FIDELIDADE_SEM_CONSENTIMENTO");
    }

    [Fact]
    public async Task CT43_Pontos_e_campanha_exigem_cliente_identificado()
    {
        var loja = await _c.CriarLojaAsync();
        await (await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 1, pontos: 10))
            .DeveFalhar(HttpStatusCode.Conflict, "CLIENTE_OBRIGATORIO");
    }
}

[Collection(ColecaoApi.Nome)]
public class CampanhasTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    private async Task<CampanhaDto> CriarCampanha(int? frequencia = null, Regiao? regiao = null, decimal percentual = 10)
    {
        var admin = await _c.AdminAsync();
        return await (await admin.Post("/api/v1/campanhas", new CriarCampanhaRequest($"Campanha {Guid.NewGuid():N}"[..20], null, percentual,
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7), frequencia, null, regiao))).Ler<CampanhaDto>(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CT45_Campanha_regional_aplicada_no_pedido()
    {
        var campanha = await CriarCampanha(regiao: Regiao.Nordeste);
        var loja = await _c.CriarLojaAsync(precos: 50m);
        var cliente = await _c.CriarClienteAsync();

        var elegiveis = await cliente.Http.GetOk<List<CampanhaDto>>($"/api/v1/clientes/me/campanhas?unidadeId={loja.Unidade.Id}");
        elegiveis.ShouldContain(c => c.Id == campanha.Id);

        var pedido = await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, 1, campanhaId: campanha.Id)).Ler<PedidoDto>(HttpStatusCode.Created);
        pedido.DescontoCampanha.ShouldBe(5m);
        pedido.CampanhaId.ShouldBe(campanha.Id);
        pedido.Total.ShouldBe(45m);
    }

    [Fact]
    public async Task CT46_Campanha_de_outra_regiao_ou_segmentada_sem_consentimento_e_recusada()
    {
        var sudeste = await CriarCampanha(regiao: Regiao.Sudeste);
        var segmentada = await CriarCampanha(frequencia: 1);
        var loja = await _c.CriarLojaAsync(precos: 50m);
        var semConsentimento = await _c.CriarClienteAsync(campanhas: false);

        await (await Cenario.CriarPedido(semConsentimento.Http, loja.Unidade.Id, loja.Produtos[0].Id, 1, campanhaId: sudeste.Id))
            .DeveFalhar(HttpStatusCode.Conflict, "CAMPANHA_INELEGIVEL");
        await (await Cenario.CriarPedido(semConsentimento.Http, loja.Unidade.Id, loja.Produtos[0].Id, 1, campanhaId: segmentada.Id))
            .DeveFalhar(HttpStatusCode.Conflict, "CAMPANHA_INELEGIVEL");
        (await semConsentimento.Http.GetOk<List<CampanhaDto>>("/api/v1/clientes/me/campanhas")).ShouldNotContain(c => c.Id == segmentada.Id);
    }
}

[Collection(ColecaoApi.Nome)]
public class PrivacidadeTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    [Fact]
    public async Task CT47_Consentimentos_versionados_e_revogaveis()
    {
        var cliente = await _c.CriarClienteAsync(fidelidade: true, campanhas: true);
        var revogado = await (await cliente.Http.Put("/api/v1/clientes/me/consentimentos",
            new AlterarConsentimentoRequest(TipoConsentimento.CampanhasPersonalizadas, false))).Ler<ConsentimentoDto>();
        revogado.Concedido.ShouldBeFalse();

        var atuais = await cliente.Http.GetOk<List<ConsentimentoDto>>("/api/v1/clientes/me/consentimentos");
        atuais.Single(c => c.Tipo == TipoConsentimento.CampanhasPersonalizadas).Concedido.ShouldBeFalse();
        atuais.Single(c => c.Tipo == TipoConsentimento.ProgramaFidelidade).Concedido.ShouldBeTrue();
        (await _c.NoBancoAsync(db => db.Consentimentos.CountAsync(c => c.ClienteId == cliente.Id))).ShouldBe(4); // histórico preservado

        await (await cliente.Http.Put("/api/v1/clientes/me/consentimentos", new AlterarConsentimentoRequest(TipoConsentimento.TermosDeUso, false)))
            .DeveFalhar(HttpStatusCode.Conflict, "TERMOS_OBRIGATORIOS");
    }

    [Fact]
    public async Task CT48_Exportacao_de_dados_do_titular_e_auditada()
    {
        var loja = await _c.CriarLojaAsync();
        var cliente = await _c.CriarClienteAsync(nascimento: new DateOnly(1990, 5, 20));
        await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id);

        var dados = await cliente.Http.GetOk<DadosTitularDto>("/api/v1/clientes/me/dados");
        dados.Cadastro.Email.ShouldBe(cliente.Email);
        dados.Cadastro.DataNascimento.ShouldBe(new DateOnly(1990, 5, 20));
        dados.Pedidos.Count.ShouldBe(1);
        dados.HistoricoConsentimentos.Count.ShouldBe(3);
        (await _c.NoBancoAsync(db => db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.DadosPessoaisExportados && a.EntidadeId == cliente.Id.ToString()))).ShouldBeTrue();
    }

    [Fact]
    public async Task CT49_Anonimizacao_remove_dados_pessoais_e_mantem_pedidos()
    {
        var loja = await _c.CriarLojaAsync();
        var cliente = await _c.CriarClienteAsync(nascimento: new DateOnly(1990, 5, 20));
        var pedido = await (await cliente.Http.Post("/api/v1/pedidos",
            new CriarPedidoRequest(loja.Unidade.Id, [new(loja.Produtos[0].Id, 1)], CanalPedido.App, NomeChamada: "Joana"), Guid.NewGuid().ToString())).Ler<PedidoDto>();

        await (await cliente.Http.DeleteAsync("/api/v1/clientes/me")).DeveFalhar(HttpStatusCode.Conflict, "PEDIDOS_EM_ANDAMENTO");
        (await cliente.Http.Post($"/api/v1/pedidos/{pedido.Id}/cancelamento", new CancelarPedidoRequest("Encerrando conta"))).EnsureSuccessStatusCode();
        (await cliente.Http.DeleteAsync("/api/v1/clientes/me")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await _c.Anonimo().Post("/api/v1/auth/login", new LoginRequest(cliente.Email, Cenario.SenhaPadrao))).DeveFalhar(HttpStatusCode.Unauthorized);
        var (usuario, dadosCliente, nomeChamada) = await _c.NoBancoAsync(async db => (
            await db.Usuarios.FirstAsync(u => u.Id == cliente.Id),
            await db.Clientes.FirstAsync(c => c.Id == cliente.Id),
            await db.Pedidos.Where(p => p.Id == pedido.Id).Select(p => p.NomeChamada).FirstAsync()));
        usuario.Email.ShouldNotBe(cliente.Email);
        usuario.Nome.ShouldBe("Cliente anonimizado");
        dadosCliente.DataNascimento.ShouldBeNull();
        dadosCliente.Anonimizado.ShouldBeTrue();
        nomeChamada.ShouldBeNull();
        (await _c.NoBancoAsync(db => db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.ClienteAnonimizado && a.EntidadeId == cliente.Id.ToString()))).ShouldBeTrue();
    }

    [Fact]
    public async Task CT50_Acesso_administrativo_a_dados_pessoais_e_auditado()
    {
        var cliente = await _c.CriarClienteAsync();
        var admin = await _c.AdminAsync();
        var dados = await admin.GetOk<ClienteAdminDto>($"/api/v1/clientes/{cliente.Id}");
        dados.Email.ShouldBe(cliente.Email);
        (await _c.NoBancoAsync(db => db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.DadosPessoaisAcessados && a.EntidadeId == cliente.Id.ToString()))).ShouldBeTrue();

        var outro = await _c.CriarClienteAsync();
        (await outro.Http.GetAsync($"/api/v1/clientes/{cliente.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
