using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Pagamentos;
using RaizesDoNordeste.Application.Pedidos;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.IntegrationTests.Infra;

namespace RaizesDoNordeste.IntegrationTests;

/// <summary>Integração com o serviço externo de pagamentos, incluindo cenários de falha.</summary>
[Collection(ColecaoApi.Nome)]
public class PagamentosTests(ApiFixture fixture)
{
    private readonly Cenario _c = new(fixture);

    private async Task<(Loja loja, PedidoDto pedido)> PedidoComTotal(decimal total)
    {
        var loja = await _c.CriarLojaAsync(estoque: 10, precos: total);
        var pedido = await (await Cenario.CriarPedido(loja.Atendente, loja.Unidade.Id, loja.Produtos[0].Id)).Ler<PedidoDto>(HttpStatusCode.Created);
        pedido.Total.ShouldBe(total);
        return (loja, pedido);
    }

    private static HttpRequestMessage Webhook(string corpo, string segredo, DateTimeOffset? quando = null)
    {
        var ts = (quando ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/pagamentos") { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Timestamp", ts);
        req.Headers.Add("X-Assinatura", AssinaturaWebhook.Calcular(segredo, ts, corpo));
        return req;
    }

    [Fact]
    public async Task CT30_Recusa_assincrona_mantem_pedido_aguardando_e_reserva()
    {
        var (loja, pedido) = await PedidoComTotal(13.13m);
        var pg = await (await loja.Atendente.Post($"/api/v1/pedidos/{pedido.Id}/pagamentos", new SolicitarPagamentoRequest(MetodoPagamento.CartaoCredito)))
            .Ler<PagamentoDto>(HttpStatusCode.Accepted);
        pg.Status.ShouldBe(StatusPagamento.Pendente);

        var atual = await Cenario.AguardarPagamentoAsync(loja.Atendente, pedido.Id, StatusPagamento.Recusado);
        atual.Status.ShouldBe(StatusPedido.AguardandoPagamento);
        atual.Pagamentos.Single().Detalhe.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CT31_Recusa_sincrona_do_gateway()
    {
        var (loja, pedido) = await PedidoComTotal(14.14m);
        var pg = await (await loja.Atendente.Post($"/api/v1/pedidos/{pedido.Id}/pagamentos", new SolicitarPagamentoRequest(MetodoPagamento.CartaoDebito)))
            .Ler<PagamentoDto>(HttpStatusCode.Accepted);
        pg.Status.ShouldBe(StatusPagamento.Recusado);
        pg.Detalhe!.ShouldContain("bloqueado");
    }

    [Fact]
    public async Task CT32_Gateway_fora_do_ar_retorna_503_com_retry_after_e_abre_circuito()
    {
        await using var api = fixture.CriarApi();
        var c = new Cenario(fixture, api);
        var loja = await c.CriarLojaAsync(estoque: 20, precos: 99.99m);

        var duracoes = new List<TimeSpan>();
        HttpResponseMessage? ultima = null;
        for (var i = 0; i < 7; i++)
        {
            var pedido = await (await Cenario.CriarPedido(loja.Atendente, loja.Unidade.Id, loja.Produtos[0].Id)).Ler<PedidoDto>();
            var inicio = DateTime.UtcNow;
            ultima = await loja.Atendente.Post($"/api/v1/pedidos/{pedido.Id}/pagamentos", new SolicitarPagamentoRequest(MetodoPagamento.Pix));
            duracoes.Add(DateTime.UtcNow - inicio);
            await ultima.DeveFalhar(HttpStatusCode.ServiceUnavailable, "SERVICO_INDISPONIVEL");
            ultima.Headers.RetryAfter.ShouldNotBeNull();
            (await loja.Atendente.GetOk<PedidoDto>($"/api/v1/pedidos/{pedido.Id}")).Pagamentos.Single().Status.ShouldBe(StatusPagamento.Falhou);
        }
        // Com o circuito aberto, a API falha rápido em vez de esperar as re-tentativas.
        duracoes[^1].ShouldBeLessThan(duracoes[0]);
    }

    [Fact]
    public async Task CT33_Timeout_com_retentativa_nao_gera_cobranca_duplicada()
    {
        var (loja, pedido) = await PedidoComTotal(77.77m);
        (await loja.Atendente.Post($"/api/v1/pedidos/{pedido.Id}/pagamentos", new SolicitarPagamentoRequest(MetodoPagamento.Pix)))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var pago = await Cenario.AguardarStatusAsync(loja.Atendente, pedido.Id, StatusPedido.Pago, 15_000);
        fixture.TransacoesDoGateway.PorReferencia(pago.Pagamentos.Single().Id).Count.ShouldBe(1);
    }

    [Fact]
    public async Task CT34_Solicitar_pagamento_duas_vezes_devolve_o_mesmo_pagamento()
    {
        var (loja, pedido) = await PedidoComTotal(13.13m); // recusa só chega depois; durante a janela fica pendente
        var tarefas = Enumerable.Range(0, 3).Select(_ =>
            loja.Atendente.Post($"/api/v1/pedidos/{pedido.Id}/pagamentos", new SolicitarPagamentoRequest(MetodoPagamento.Pix))).ToList();
        var respostas = await Task.WhenAll(tarefas);
        var ids = new HashSet<Guid>();
        foreach (var r in respostas) ids.Add((await r.Ler<PagamentoDto>(HttpStatusCode.Accepted)).Id);
        ids.Count.ShouldBe(1);
        (await _c.NoBancoAsync(db => db.Pagamentos.CountAsync(p => p.PedidoId == pedido.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task CT35_Pagamento_em_pedido_ja_pago_e_rejeitado()
    {
        var loja = await _c.CriarLojaAsync();
        var pago = await Cenario.CriarPedidoPagoAsync(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id);
        await (await loja.Totem.Post($"/api/v1/pedidos/{pago.Id}/pagamentos", new SolicitarPagamentoRequest(MetodoPagamento.Pix)))
            .DeveFalhar(HttpStatusCode.Conflict, "PEDIDO_NAO_AGUARDA_PAGAMENTO");
    }

    [Fact]
    public async Task CT36_Webhook_com_assinatura_invalida_ou_antiga_e_rejeitado()
    {
        var http = _c.Anonimo();
        var corpo = $$"""{"referencia":"{{Guid.NewGuid()}}","transacaoId":"tx_falso","status":"aprovado"}""";
        await (await http.SendAsync(Webhook(corpo, "segredo-errado"))).DeveFalhar(HttpStatusCode.Unauthorized);
        await (await http.SendAsync(Webhook(corpo, ApiFixture.SegredoWebhook, DateTimeOffset.UtcNow.AddMinutes(-10)))).DeveFalhar(HttpStatusCode.Unauthorized);
        var semHeaders = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/pagamentos") { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        await (await http.SendAsync(semHeaders)).DeveFalhar(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CT37_Webhook_repetido_e_idempotente()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var pago = await Cenario.CriarPedidoPagoAsync(loja.Totem, loja.Unidade.Id, loja.Produtos[0].Id, 2);
        var pg = pago.Pagamentos.Single();
        var tx = await _c.NoBancoAsync(db => db.Pagamentos.Where(p => p.Id == pg.Id).Select(p => p.TransacaoExternaId).FirstAsync());

        var corpo = $$"""{"referencia":"{{pg.Id}}","transacaoId":"{{tx}}","status":"aprovado"}""";
        (await _c.Anonimo().SendAsync(Webhook(corpo, ApiFixture.SegredoWebhook))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _c.Anonimo().SendAsync(Webhook(corpo.Replace("aprovado", "recusado"), ApiFixture.SegredoWebhook))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var estoque = await _c.NoBancoAsync(db => db.Estoques.FirstAsync(e => e.UnidadeId == loja.Unidade.Id && e.ProdutoId == loja.Produtos[0].Id));
        estoque.Quantidade.ShouldBe(8); // baixado uma única vez
        (await loja.Totem.GetOk<PedidoDto>($"/api/v1/pedidos/{pago.Id}")).Pagamentos.Single().Status.ShouldBe(StatusPagamento.Aprovado);
    }

    [Fact]
    public async Task CT38_Aprovacao_que_chega_depois_do_cancelamento_e_estornada_automaticamente()
    {
        var loja = await _c.CriarLojaAsync(estoque: 10);
        var pedido = await (await Cenario.CriarPedido(loja.Atendente, loja.Unidade.Id, loja.Produtos[0].Id)).Ler<PedidoDto>();

        // Simula a corrida: pagamento enviado ao gateway, cliente cancela antes de o webhook chegar.
        var pagamentoId = await _c.NoBancoAsync(async db =>
        {
            var p = new Pagamento(pedido.Id, pedido.Total, MetodoPagamento.Pix, DateTimeOffset.UtcNow);
            var tx = fixture.TransacoesDoGateway.RegistrarAprovada(p.Id, p.Valor);
            p.RegistrarEnvio(tx.Id, DateTimeOffset.UtcNow);
            db.Pagamentos.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });
        (await loja.Atendente.Post($"/api/v1/pedidos/{pedido.Id}/cancelamento", new CancelarPedidoRequest("Cliente foi embora"))).EnsureSuccessStatusCode();

        var tx = fixture.TransacoesDoGateway.PorReferencia(pagamentoId).Single();
        var corpo = $$"""{"referencia":"{{pagamentoId}}","transacaoId":"{{tx.Id}}","status":"aprovado"}""";
        (await _c.Anonimo().SendAsync(Webhook(corpo, ApiFixture.SegredoWebhook))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var final = await loja.Atendente.GetOk<PedidoDto>($"/api/v1/pedidos/{pedido.Id}");
        final.Status.ShouldBe(StatusPedido.Cancelado);
        final.Pagamentos.Single().Status.ShouldBe(StatusPagamento.Estornado);
        tx.Status.ShouldBe("estornado");
    }
}
