using System.Net;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Estoques;
using RaizesDoNordeste.Application.Relatorios;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Unidades;
using RaizesDoNordeste.IntegrationTests.Infra;

namespace RaizesDoNordeste.IntegrationTests;

[Collection(ColecaoApi.Nome)]
public class EstoqueTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    [Fact]
    public async Task CT51_Gerente_so_gerencia_a_propria_unidade()
    {
        var loja = await _c.CriarLojaAsync();
        var outra = await _c.CriarLojaAsync();
        await (await loja.Gerente.GetAsync($"/api/v1/unidades/{outra.Unidade.Id}/estoque")).DeveFalhar(HttpStatusCode.Forbidden, "ACESSO_NEGADO");
        await (await loja.Gerente.Post($"/api/v1/unidades/{outra.Unidade.Id}/estoque/movimentacoes",
            new MovimentarEstoqueRequest(outra.Produtos[0].Id, TipoMovimentacaoManual.Entrada, 5, null))).DeveFalhar(HttpStatusCode.Forbidden);
        await (await loja.Atendente.Post($"/api/v1/unidades/{loja.Unidade.Id}/estoque/movimentacoes",
            new MovimentarEstoqueRequest(loja.Produtos[0].Id, TipoMovimentacaoManual.Entrada, 5, null))).DeveFalhar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CT52_Perda_exige_motivo_e_e_auditada()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var url = $"/api/v1/unidades/{loja.Unidade.Id}/estoque/movimentacoes";
        await (await loja.Gerente.Post(url, new MovimentarEstoqueRequest(loja.Produtos[0].Id, TipoMovimentacaoManual.Perda, 2, null)))
            .DeveFalhar(HttpStatusCode.BadRequest, "VALIDACAO");

        var item = await (await loja.Gerente.Post(url, new MovimentarEstoqueRequest(loja.Produtos[0].Id, TipoMovimentacaoManual.Perda, 2, "Validade vencida")))
            .Ler<ItemEstoqueDto>(HttpStatusCode.Created);
        item.Quantidade.ShouldBe(8);
        (await _c.NoBancoAsync(db => db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.EstoqueAjustado && a.UnidadeId == loja.Unidade.Id))).ShouldBeTrue();
        (await _c.NoBancoAsync(db => db.MovimentacoesEstoque.Where(m => m.UnidadeId == loja.Unidade.Id && m.ProdutoId == loja.Produtos[0].Id).SumAsync(m => m.Quantidade)))
            .ShouldBe(8); // o extrato de movimentações fecha com o saldo
    }

    [Fact]
    public async Task CT53_Ajuste_nao_pode_comprometer_estoque_reservado()
    {
        var loja = await _c.CriarLojaAsync(estoque: 5);
        (await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 4)).EnsureSuccessStatusCode();
        await (await loja.Gerente.Post($"/api/v1/unidades/{loja.Unidade.Id}/estoque/movimentacoes",
            new MovimentarEstoqueRequest(loja.Produtos[0].Id, TipoMovimentacaoManual.Ajuste, -3, "Inventário")))
            .DeveFalhar(HttpStatusCode.Conflict, "AJUSTE_INVALIDO");
    }

    [Fact]
    public async Task CT54_Alerta_de_estoque_minimo()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        (await loja.Gerente.Put($"/api/v1/unidades/{loja.Unidade.Id}/estoque/{loja.Produtos[0].Id}/minimo", new DefinirEstoqueMinimoRequest(12)))
            .EnsureSuccessStatusCode();
        var abaixo = await loja.Gerente.GetOk<List<ItemEstoqueDto>>($"/api/v1/unidades/{loja.Unidade.Id}/estoque?abaixoDoMinimo=true");
        abaixo.Select(i => i.ProdutoId).ShouldBe([loja.Produtos[0].Id]);
    }
}

[Collection(ColecaoApi.Nome)]
public class RelatoriosTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    [Fact]
    public async Task CT55_Vendas_e_ranking_consolidados_por_unidade()
    {
        var loja = await _c.CriarLojaAsync(estoque: 20, precos: [10m, 25m]);
        await Cenario.CriarPedidoPagoAsync(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 3); // 30
        await Cenario.CriarPedidoPagoAsync(loja.Totem, loja.Unidade.Id, loja.Produtos[1].Id, 1); // 25
        (await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[1].Id, 5)).EnsureSuccessStatusCode(); // não pago: fora do relatório

        var admin = await _c.AdminAsync();
        var vendas = await admin.GetOk<RelatorioVendasDto>($"/api/v1/relatorios/vendas?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}&agrupamento=Unidade&unidadeId={loja.Unidade.Id}");
        var linha = vendas.Linhas.Single();
        linha.Pedidos.ShouldBe(2);
        linha.Faturamento.ShouldBe(55m);
        linha.TicketMedio.ShouldBe(27.5m);

        var ranking = await admin.GetOk<List<ProdutoMaisVendidoDto>>($"/api/v1/relatorios/produtos-mais-vendidos?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}&unidadeId={loja.Unidade.Id}");
        ranking.Select(r => r.ProdutoId).ShouldBe([loja.Produtos[0].Id, loja.Produtos[1].Id]);
        ranking[0].Quantidade.ShouldBe(3);

        var porRegiao = await admin.GetOk<RelatorioVendasDto>($"/api/v1/relatorios/vendas?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}&agrupamento=Regiao");
        porRegiao.Linhas.ShouldContain(l => l.Chave == nameof(Regiao.Nordeste));
    }

    [Fact]
    public async Task CT56_Gerente_ve_apenas_a_propria_unidade()
    {
        var loja = await _c.CriarLojaAsync();
        var outra = await _c.CriarLojaAsync();
        await Cenario.CriarPedidoPagoAsync(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id);

        var proprio = await loja.Gerente.GetOk<RelatorioVendasDto>($"/api/v1/relatorios/vendas?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}&agrupamento=Unidade");
        proprio.Linhas.Single().Chave.ShouldBe(loja.Unidade.Id.ToString());

        await (await loja.Gerente.GetAsync($"/api/v1/relatorios/vendas?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}&agrupamento=Unidade&unidadeId={outra.Unidade.Id}"))
            .DeveFalhar(HttpStatusCode.Forbidden);
        await (await loja.Gerente.GetAsync($"/api/v1/relatorios/vendas?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}&agrupamento=Regiao"))
            .DeveFalhar(HttpStatusCode.Forbidden);
        await (await loja.Atendente.GetAsync($"/api/v1/relatorios/vendas?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}&agrupamento=Unidade"))
            .DeveFalhar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CT57_Periodo_invalido_e_rejeitado()
    {
        var admin = await _c.AdminAsync();
        await (await admin.GetAsync($"/api/v1/relatorios/vendas?de={Hoje:yyyy-MM-dd}&ate={Hoje.AddDays(-1):yyyy-MM-dd}&agrupamento=Unidade"))
            .DeveFalhar(HttpStatusCode.BadRequest, "VALIDACAO");
    }

    [Fact]
    public async Task CT58_Trilha_de_auditoria_filtrada_e_paginada()
    {
        var loja = await _c.CriarLojaAsync();
        (await loja.Gerente.Post($"/api/v1/unidades/{loja.Unidade.Id}/estoque/movimentacoes",
            new MovimentarEstoqueRequest(loja.Produtos[0].Id, TipoMovimentacaoManual.Ajuste, 1, "Contagem"))).EnsureSuccessStatusCode();
        var admin = await _c.AdminAsync();
        var pagina = await admin.GetOk<ResultadoPaginado<RegistroAuditoriaDto>>($"/api/v1/auditoria?acao={AcoesAuditoria.EstoqueAjustado}&unidadeId={loja.Unidade.Id}&tamanho=5");
        pagina.Total.ShouldBe(1);
        pagina.Itens.Single().Detalhes!.ShouldContain("Contagem");

        var resumo = await loja.Gerente.GetOk<List<ResumoAuditoriaDto>>($"/api/v1/relatorios/operacoes-sensiveis?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}");
        resumo.ShouldContain(r => r.Acao == AcoesAuditoria.EstoqueAjustado && r.Ocorrencias == 1);
    }
}
