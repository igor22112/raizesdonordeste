using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaizesDoNordeste.Application.Catalogo;
using RaizesDoNordeste.Application.Estoques;
using RaizesDoNordeste.Application.Pedidos;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.Domain.Unidades;
using RaizesDoNordeste.IntegrationTests.Infra;

namespace RaizesDoNordeste.IntegrationTests;

[Collection(ColecaoApi.Nome)]
public class CardapioTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    [Fact]
    public async Task CT12_Cardapio_respeita_tipo_de_cozinha_sazonalidade_preco_local_e_estoque()
    {
        var unidade = await _c.CriarUnidadeAsync(TipoUnidade.CozinhaCompleta);
        var normal = await _c.CriarProdutoAsync(10m);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var foraDeEpoca = await _c.CriarProdutoAsync(9m, sazonalInicio: hoje.AddMonths(2).ToString("MM-dd"), sazonalFim: hoje.AddMonths(3).ToString("MM-dd"));
        var semEstoque = await _c.CriarProdutoAsync(7m);
        foreach (var p in new[] { normal, foraDeEpoca, semEstoque }) await _c.LiberarNoCardapioAsync(unidade.Id, p.Id);
        await _c.LiberarNoCardapioAsync(unidade.Id, normal.Id, precoLocal: 12.5m);
        await _c.EntradaEstoqueAsync(unidade.Id, normal.Id, 10);

        var cardapio = await _c.Anonimo().GetOk<CardapioDto>($"/api/v1/unidades/{unidade.Id}/cardapio");

        cardapio.Itens.Select(i => i.ProdutoId).ShouldBe([normal.Id, semEstoque.Id], ignoreOrder: true);
        cardapio.Itens.Single(i => i.ProdutoId == normal.Id).Preco.ShouldBe(12.5m);
        cardapio.Itens.Single(i => i.ProdutoId == semEstoque.Id).Esgotado.ShouldBeTrue();
    }

    [Fact]
    public async Task CT13_Unidade_reduzida_nao_aceita_produto_de_cozinha_completa()
    {
        var reduzida = await _c.CriarUnidadeAsync(TipoUnidade.CozinhaReduzida);
        var cuscuz = await _c.CriarProdutoAsync(12m, requerCozinhaCompleta: true);
        var admin = await _c.AdminAsync();
        var r = await admin.Put($"/api/v1/unidades/{reduzida.Id}/cardapio/{cuscuz.Id}", new ConfigurarItemCardapioRequest(true, null, null));
        await r.DeveFalhar(HttpStatusCode.Conflict, "PRODUTO_INCOMPATIVEL_UNIDADE");
    }

    [Fact]
    public async Task CT14_Alteracao_de_preco_local_e_auditada()
    {
        var loja = await _c.CriarLojaAsync();
        (await loja.Gerente.Put($"/api/v1/unidades/{loja.Unidade.Id}/cardapio/{loja.Produtos[0].Id}",
            new ConfigurarItemCardapioRequest(true, 11.9m, "Receita regional"))).EnsureSuccessStatusCode();
        var auditado = await _c.NoBancoAsync(db => db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.PrecoLocalAlterado && a.UnidadeId == loja.Unidade.Id));
        auditado.ShouldBeTrue();
    }
}

[Collection(ColecaoApi.Nome)]
public class CriacaoPedidoTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    private Task<ItemEstoqueDto> Estoque(Loja loja, Guid produtoId) =>
        loja.Gerente.GetOk<List<ItemEstoqueDto>>($"/api/v1/unidades/{loja.Unidade.Id}/estoque").ContinueWith(t => t.Result.Single(e => e.ProdutoId == produtoId));

    [Fact]
    public async Task CT15_Criar_pedido_reserva_estoque_e_define_prazo_de_pagamento()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var cliente = await _c.CriarClienteAsync();
        var pedido = await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, 3)).Ler<PedidoDto>(HttpStatusCode.Created);

        pedido.Status.ShouldBe(StatusPedido.AguardandoPagamento);
        pedido.CanalPedido.ShouldBe(CanalPedido.App);
        pedido.Total.ShouldBe(30m);
        (pedido.ExpiraEm - pedido.CriadoEm).ShouldBe(TimeSpan.FromMinutes(15));
        var estoque = await Estoque(loja, loja.Produtos[0].Id);
        estoque.Reservada.ShouldBe(3);
        estoque.Disponivel.ShouldBe(7);
    }

    [Fact]
    public async Task CT16_Mesma_chave_de_idempotencia_nao_duplica_pedido()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var chave = Guid.NewGuid().ToString();
        var r1 = await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 2, chave: chave);
        var r2 = await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 2, chave: chave);

        r1.StatusCode.ShouldBe(HttpStatusCode.Created);
        r2.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await r2.Ler<PedidoDto>()).Id.ShouldBe((await r1.Ler<PedidoDto>()).Id);
        (await Estoque(loja, loja.Produtos[0].Id)).Reservada.ShouldBe(2);
    }

    [Fact]
    public async Task CT17_Sem_idempotency_key_retorna_400()
    {
        var loja = await _c.CriarLojaAsync();
        var r = await loja.Totem.Post("/api/v1/pedidos", new CriarPedidoRequest(loja.Unidade.Id, [new(loja.Produtos[0].Id, 1)], CanalPedido.Totem));
        await r.DeveFalhar(HttpStatusCode.BadRequest, "VALIDACAO");
    }

    [Fact]
    public async Task CT18_Estoque_insuficiente_retorna_422()
    {
        var loja = await _c.CriarLojaAsync(estoque: 2);
        var r = await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 3);
        await r.DeveFalhar(HttpStatusCode.Conflict, "ESTOQUE_INSUFICIENTE");
    }

    [Fact]
    public async Task CT19_Pedidos_simultaneos_nao_vendem_acima_do_estoque()
    {
        var loja = await _c.CriarLojaAsync(estoque: 5);
        var tarefas = Enumerable.Range(0, 12).Select(_ => Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 1)).ToList();
        var respostas = await Task.WhenAll(tarefas);

        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(5);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);
        var estoque = await Estoque(loja, loja.Produtos[0].Id);
        estoque.Reservada.ShouldBe(5);
        estoque.Disponivel.ShouldBe(0);
    }

    [Fact]
    public async Task CT20_Unidade_fechada_nao_aceita_pedidos()
    {
        var agoraRecife = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Recife")).DateTime);
        var unidade = await _c.CriarUnidadeAsync(abre: agoraRecife.AddHours(2), fecha: agoraRecife.AddHours(3));
        var produto = await _c.CriarProdutoAsync(10m);
        await _c.LiberarNoCardapioAsync(unidade.Id, produto.Id);
        await _c.EntradaEstoqueAsync(unidade.Id, produto.Id, 5);
        var cliente = await _c.CriarClienteAsync();

        var r = await Cenario.CriarPedido(cliente.Http, unidade.Id, produto.Id);
        await r.DeveFalhar(HttpStatusCode.Conflict, "UNIDADE_FECHADA");
    }

    [Fact]
    public async Task CT21_Produto_fora_da_temporada_nao_pode_ser_pedido()
    {
        var loja = await _c.CriarLojaAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var junino = await _c.CriarProdutoAsync(9.9m, sazonalInicio: hoje.AddMonths(2).ToString("MM-dd"), sazonalFim: hoje.AddMonths(3).ToString("MM-dd"));
        await _c.LiberarNoCardapioAsync(loja.Unidade.Id, junino.Id);
        await _c.EntradaEstoqueAsync(loja.Unidade.Id, junino.Id, 5);

        var r = await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, junino.Id);
        await r.DeveFalhar(HttpStatusCode.Conflict, "PRODUTO_FORA_DE_TEMPORADA");
    }

    [Fact]
    public async Task CT22_CanalPedido_registrado_e_compativel_com_o_perfil_e_totem_so_atende_sua_unidade()
    {
        var loja = await _c.CriarLojaAsync();
        var outra = await _c.CriarLojaAsync();

        (await (await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id)).Ler<PedidoDto>()).CanalPedido.ShouldBe(CanalPedido.Totem);
        (await (await Cenario.CriarPedido(loja.Atendente, loja.Unidade.Id, loja.Produtos[0].Id)).Ler<PedidoDto>()).CanalPedido.ShouldBe(CanalPedido.Balcao);

        var cliente = await _c.CriarClienteAsync();
        foreach (var canal in new[] { CanalPedido.App, CanalPedido.Web, CanalPedido.Pickup })
            (await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, canal: canal)).Ler<PedidoDto>()).CanalPedido.ShouldBe(canal);

        // Cliente não pode se declarar totem; totem não pode se declarar app.
        await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, canal: CanalPedido.Totem))
            .DeveFalhar(HttpStatusCode.Conflict, "CANAL_INCOMPATIVEL");
        await (await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, canal: CanalPedido.App))
            .DeveFalhar(HttpStatusCode.Conflict, "CANAL_INCOMPATIVEL");

        await (await Cenario.CriarPedido(loja.Totem, outra.Unidade.Id, outra.Produtos[0].Id)).DeveFalhar(HttpStatusCode.Forbidden, "ACESSO_NEGADO");
        await (await Cenario.CriarPedido(loja.Cozinha, loja.Unidade.Id, loja.Produtos[0].Id)).DeveFalhar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CT23_Cliente_nao_acessa_pedido_de_outro_cliente()
    {
        var loja = await _c.CriarLojaAsync();
        var dono = await _c.CriarClienteAsync();
        var intruso = await _c.CriarClienteAsync();
        var pedido = await (await Cenario.CriarPedido(dono.Http, loja.Unidade.Id, loja.Produtos[0].Id)).Ler<PedidoDto>();

        await (await intruso.Http.GetAsync($"/api/v1/pedidos/{pedido.Id}")).DeveFalhar(HttpStatusCode.NotFound);
        var lista = await intruso.Http.GetOk<RaizesDoNordeste.Application.Comum.ResultadoPaginado<PedidoResumoDto>>("/api/v1/pedidos");
        lista.Itens.ShouldNotContain(p => p.Id == pedido.Id);
    }

    [Fact]
    public async Task CT60_CanalPedido_ausente_ou_invalido_retorna_400_no_formato_padrao()
    {
        var loja = await _c.CriarLojaAsync();
        var ausente = await loja.Totem.Post("/api/v1/pedidos", new { unidadeId = loja.Unidade.Id, itens = new[] { new { produtoId = loja.Produtos[0].Id, quantidade = 1 } } },
            Guid.NewGuid().ToString());
        await ausente.DeveFalhar(HttpStatusCode.BadRequest, "VALIDACAO");
        (await ausente.Content.ReadAsStringAsync()).ShouldContain("canalPedido");

        var invalido = await loja.Totem.Post("/api/v1/pedidos", new { canalPedido = "FAX", unidadeId = loja.Unidade.Id, itens = new[] { new { produtoId = loja.Produtos[0].Id, quantidade = 1 } } },
            Guid.NewGuid().ToString());
        await invalido.DeveFalhar(HttpStatusCode.BadRequest, "VALIDACAO");
        var corpo = await invalido.Content.ReadAsStringAsync();
        corpo.ShouldContain("\"timestamp\"");
        corpo.ShouldContain("\"instance\":\"/api/v1/pedidos\"");
    }

    [Fact]
    public async Task CT61_Consulta_de_pedidos_filtrada_por_canal()
    {
        var loja = await _c.CriarLojaAsync(estoque: 20);
        var totem = await (await Cenario.CriarPedido(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id)).Ler<PedidoDto>();
        var balcao = await (await Cenario.CriarPedido(loja.Atendente, loja.Unidade.Id, loja.Produtos[0].Id)).Ler<PedidoDto>();

        var soTotem = await loja.Gerente.GetOk<RaizesDoNordeste.Application.Comum.ResultadoPaginado<PedidoResumoDto>>(
            "/api/v1/pedidos?canalPedido=TOTEM&status=AguardandoPagamento&page=1&limit=10");
        soTotem.Itens.ShouldContain(p => p.Id == totem.Id);
        soTotem.Itens.ShouldNotContain(p => p.Id == balcao.Id);
        soTotem.Itens.ShouldAllBe(p => p.CanalPedido == CanalPedido.Totem);
        soTotem.Tamanho.ShouldBe(10);
    }

    [Fact]
    public async Task CT62_Produto_ou_unidade_inexistente_retorna_404()
    {
        var loja = await _c.CriarLojaAsync();
        await (await Cenario.CriarPedido(loja.Atendente, loja.Unidade.Id, Guid.NewGuid())).DeveFalhar(HttpStatusCode.NotFound, "NAO_ENCONTRADO");
        var cliente = await _c.CriarClienteAsync();
        await (await Cenario.CriarPedido(cliente.Http, Guid.NewGuid(), loja.Produtos[0].Id)).DeveFalhar(HttpStatusCode.NotFound, "NAO_ENCONTRADO");
    }

    [Fact]
    public async Task CT63_Criacao_e_mudanca_de_status_do_pedido_sao_auditadas()
    {
        var loja = await _c.CriarLojaAsync();
        var pago = await Cenario.CriarPedidoPagoAsync(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id);
        (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.EmPreparo))).EnsureSuccessStatusCode();

        var acoes = await _c.NoBancoAsync(db => db.Auditoria.Where(a => a.EntidadeId == pago.Id.ToString()).OrderBy(a => a.OcorridoEm).Select(a => a.Acao).ToListAsync());
        acoes.ShouldBe([AcoesAuditoria.PedidoCriado, AcoesAuditoria.PedidoStatusAlterado, AcoesAuditoria.PedidoStatusAlterado]);
    }
}

[Collection(ColecaoApi.Nome)]
public class FluxoPedidoTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    private Task<ItemEstoqueDto> Estoque(Loja loja, Guid produtoId) =>
        loja.Gerente.GetOk<List<ItemEstoqueDto>>($"/api/v1/unidades/{loja.Unidade.Id}/estoque").ContinueWith(t => t.Result.Single(e => e.ProdutoId == produtoId));

    [Fact]
    public async Task CT24_Fluxo_completo_pagamento_webhook_preparo_entrega_e_pontos()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var cliente = await _c.CriarClienteAsync();
        var produto = loja.Produtos[1]; // R$ 20

        var pago = await Cenario.CriarPedidoPagoAsync(cliente.Http, loja.Unidade.Id, produto.Id, 2);
        pago.Pagamentos.Single().Status.ShouldBe(StatusPagamento.Aprovado);
        var estoque = await Estoque(loja, produto.Id);
        estoque.Quantidade.ShouldBe(8);
        estoque.Reservada.ShouldBe(0);

        (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.EmPreparo))).EnsureSuccessStatusCode();
        (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.Pronto))).EnsureSuccessStatusCode();
        var entregue = await (await loja.Atendente.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.Entregue))).Ler<PedidoDto>();

        entregue.Status.ShouldBe(StatusPedido.Entregue);
        entregue.Historico.Select(h => h.Para).ShouldBe([StatusPedido.AguardandoPagamento, StatusPedido.Pago, StatusPedido.EmPreparo, StatusPedido.Pronto, StatusPedido.Entregue]);

        var fidelidade = await cliente.Http.GetOk<RaizesDoNordeste.Application.Fidelidade.FidelidadeDto>("/api/v1/clientes/me/fidelidade");
        fidelidade.SaldoPontos.ShouldBe(40);
        fidelidade.PedidosNosUltimos90Dias.ShouldBe(1);

        var eventos = await _c.NoBancoAsync(db => db.Outbox.Where(o => o.Payload.Contains(pago.Id.ToString())).CountAsync());
        eventos.ShouldBe(5); // criado + 4 mudanças de status
    }

    [Fact]
    public async Task CT25_Somente_perfis_autorizados_movem_o_status()
    {
        var loja = await _c.CriarLojaAsync();
        var pago = await Cenario.CriarPedidoPagoAsync(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id);

        await (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.Pago)))
            .DeveFalhar(HttpStatusCode.Conflict, "STATUS_NAO_PERMITIDO");
        await (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.Pronto)))
            .DeveFalhar(HttpStatusCode.Conflict, "TRANSICAO_INVALIDA");
        (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.EmPreparo))).EnsureSuccessStatusCode();
        (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.Pronto))).EnsureSuccessStatusCode();
        await (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.Entregue)))
            .DeveFalhar(HttpStatusCode.Forbidden);

        var outraLoja = await _c.CriarLojaAsync();
        await (await outraLoja.Atendente.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.Entregue)))
            .DeveFalhar(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CT26_Cancelar_antes_do_pagamento_libera_a_reserva_e_audita()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var cliente = await _c.CriarClienteAsync();
        var pedido = await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, 4)).Ler<PedidoDto>();

        var cancelado = await (await cliente.Http.Post($"/api/v1/pedidos/{pedido.Id}/cancelamento", new CancelarPedidoRequest("Mudei de ideia"))).Ler<PedidoDto>();

        cancelado.Status.ShouldBe(StatusPedido.Cancelado);
        cancelado.MotivoCancelamento.ShouldBe("Mudei de ideia");
        (await Estoque(loja, loja.Produtos[0].Id)).Reservada.ShouldBe(0);
        (await _c.NoBancoAsync(db => db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.PedidoCancelado && a.EntidadeId == pedido.Id.ToString()))).ShouldBeTrue();
    }

    [Fact]
    public async Task CT27_Cancelar_pedido_pago_devolve_estoque_e_estorna_no_gateway()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var pago = await Cenario.CriarPedidoPagoAsync(loja.Atendente, loja.Unidade.Id, loja.Produtos[0].Id, 2);
        (await Estoque(loja, loja.Produtos[0].Id)).Quantidade.ShouldBe(8);

        var cancelado = await (await loja.Atendente.Post($"/api/v1/pedidos/{pago.Id}/cancelamento", new CancelarPedidoRequest("Cliente desistiu"))).Ler<PedidoDto>();

        cancelado.Status.ShouldBe(StatusPedido.Cancelado);
        cancelado.Pagamentos.Single().Status.ShouldBe(StatusPagamento.Estornado);
        (await Estoque(loja, loja.Produtos[0].Id)).Quantidade.ShouldBe(10);
        fixture.TransacoesDoGateway.PorReferencia(pago.Pagamentos[0].Id).Single().Status.ShouldBe("estornado");
        (await _c.NoBancoAsync(db => db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.PagamentoEstornado && a.EntidadeId == pago.Pagamentos[0].Id.ToString()))).ShouldBeTrue();
    }

    [Fact]
    public async Task CT28_Em_preparo_so_a_gerencia_cancela_e_nao_ha_devolucao_ao_estoque()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var pago = await Cenario.CriarPedidoPagoAsync(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 1);
        (await loja.Cozinha.Patch($"/api/v1/pedidos/{pago.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.EmPreparo))).EnsureSuccessStatusCode();

        await (await loja.Atendente.Post($"/api/v1/pedidos/{pago.Id}/cancelamento", new CancelarPedidoRequest("Cliente desistiu")))
            .DeveFalhar(HttpStatusCode.Conflict, "CANCELAMENTO_EXIGE_GERENCIA");
        await (await loja.Cozinha.Post($"/api/v1/pedidos/{pago.Id}/cancelamento", new CancelarPedidoRequest("x123")))
            .DeveFalhar(HttpStatusCode.Forbidden);

        var cancelado = await (await loja.Gerente.Post($"/api/v1/pedidos/{pago.Id}/cancelamento", new CancelarPedidoRequest("Erro no preparo"))).Ler<PedidoDto>();
        cancelado.Status.ShouldBe(StatusPedido.Cancelado);
        cancelado.Pagamentos.Single().Status.ShouldBe(StatusPagamento.Estornado);
        (await Estoque(loja, loja.Produtos[0].Id)).Quantidade.ShouldBe(9); // insumo perdido
    }

    [Fact]
    public async Task CT29_Pedido_nao_pago_no_prazo_expira_e_libera_estoque_e_pontos()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var cliente = await _c.CriarClienteAsync();
        var p0 = await Cenario.CriarPedidoPagoAsync(cliente.Http, loja.Unidade.Id, loja.Produtos[1].Id); // R$ 20
        foreach (var s in new[] { StatusPedido.EmPreparo, StatusPedido.Pronto }) await loja.Cozinha.Patch($"/api/v1/pedidos/{p0.Id}/status", new AtualizarStatusPedidoRequest(s));
        await loja.Atendente.Patch($"/api/v1/pedidos/{p0.Id}/status", new AtualizarStatusPedidoRequest(StatusPedido.Entregue)); // +20 pontos

        var pedido = await (await Cenario.CriarPedido(cliente.Http, loja.Unidade.Id, loja.Produtos[0].Id, 3, pontos: 20)).Ler<PedidoDto>();
        pedido.PontosResgatados.ShouldBe(20);
        await _c.NoBancoAsync(db => db.Pedidos.Where(p => p.Id == pedido.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiraEm, DateTimeOffset.UtcNow.AddMinutes(-1))));

        var expirados = await _c.NoBancoAsync(async _ =>
        {
            using var scope = _c.Api.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ExpiracaoPedidosService>().ExpirarVencidosAsync(100, CancellationToken.None);
        });

        expirados.ShouldBeGreaterThanOrEqualTo(1);
        (await cliente.Http.GetOk<PedidoDto>($"/api/v1/pedidos/{pedido.Id}")).Status.ShouldBe(StatusPedido.Expirado);
        (await Estoque(loja, loja.Produtos[0].Id)).Reservada.ShouldBe(0);
        (await cliente.Http.GetOk<RaizesDoNordeste.Application.Fidelidade.FidelidadeDto>("/api/v1/clientes/me/fidelidade")).SaldoPontos.ShouldBe(20);
    }
}
