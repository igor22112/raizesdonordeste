using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.UnitTests.Dominio;

public class PedidoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Usuario = Guid.NewGuid();

    internal static Pedido NovoPedido(CanalPedido canal = CanalPedido.Balcao, Guid? clienteId = null, decimal preco = 10m, int qtd = 2)
    {
        var itens = new[] { new ItemNovoPedido(Guid.NewGuid(), "Tapioca", preco, qtd) };
        var precificacao = CalculadoraPedido.Calcular(preco * qtd, 0, 0, 0, 0);
        return Pedido.Criar(Guid.NewGuid(), clienteId, canal, Usuario, "chave-1", itens, precificacao, null, null, null, Agora, TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Criar_pedido_inicia_aguardando_pagamento_com_prazo_historico_e_evento()
    {
        var p = NovoPedido();

        p.Status.ShouldBe(StatusPedido.AguardandoPagamento);
        p.Subtotal.ShouldBe(20m);
        p.Total.ShouldBe(20m);
        p.ExpiraEm.ShouldBe(Agora.AddMinutes(15));
        p.Historico.Single().Para.ShouldBe(StatusPedido.AguardandoPagamento);
        p.Eventos.OfType<PedidoCriado>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(CanalPedido.App)]
    [InlineData(CanalPedido.Web)]
    [InlineData(CanalPedido.Pickup)]
    public void Pedido_de_app_web_ou_pickup_exige_cliente_identificado(CanalPedido canal)
    {
        var ex = Should.Throw<DomainException>(() => NovoPedido(canal, clienteId: null));
        ex.Codigo.ShouldBe("CLIENTE_OBRIGATORIO");
    }

    [Theory]
    [InlineData(CanalPedido.Totem)]
    [InlineData(CanalPedido.Balcao)]
    public void Totem_e_balcao_aceitam_cliente_anonimo(CanalPedido canal) =>
        NovoPedido(canal, clienteId: null).ClienteId.ShouldBeNull();

    [Fact]
    public void Itens_duplicados_sao_rejeitados()
    {
        var produto = Guid.NewGuid();
        var itens = new[] { new ItemNovoPedido(produto, "A", 5, 1), new ItemNovoPedido(produto, "A", 5, 1) };
        var ex = Should.Throw<DomainException>(() => Pedido.Criar(Guid.NewGuid(), null, CanalPedido.Totem, Usuario, "k", itens,
            CalculadoraPedido.Calcular(10, 0, 0, 0, 0), null, null, null, Agora, TimeSpan.FromMinutes(15)));
        ex.Codigo.ShouldBe("ITEM_DUPLICADO");
    }

    [Theory]
    [InlineData(21)]
    [InlineData(100)]
    public void Quantidade_acima_do_limite_e_rejeitada(int qtd) =>
        Should.Throw<DomainException>(() => NovoPedido(qtd: qtd)).Codigo.ShouldBe("QUANTIDADE_INVALIDA");

    [Fact]
    public void Fluxo_feliz_percorre_todos_os_status_e_registra_historico()
    {
        var p = NovoPedido();
        p.ConfirmarPagamento(Agora.AddMinutes(1));
        p.IniciarPreparo(Usuario, Agora.AddMinutes(2));
        p.MarcarPronto(Usuario, Agora.AddMinutes(8));
        p.Entregar(Usuario, Agora.AddMinutes(10));

        p.Status.ShouldBe(StatusPedido.Entregue);
        p.EstaFinalizado.ShouldBeTrue();
        p.Historico.Select(h => h.Para).ShouldBe([
            StatusPedido.AguardandoPagamento, StatusPedido.Pago, StatusPedido.EmPreparo, StatusPedido.Pronto, StatusPedido.Entregue]);
        p.Eventos.OfType<PedidoStatusAlterado>().Count().ShouldBe(4);
    }

    [Fact]
    public void Nao_pode_iniciar_preparo_sem_pagamento()
    {
        var ex = Should.Throw<DomainException>(() => NovoPedido().IniciarPreparo(Usuario, Agora));
        ex.Codigo.ShouldBe("TRANSICAO_INVALIDA");
    }

    [Fact]
    public void Nao_pode_entregar_pedido_que_nao_esta_pronto()
    {
        var p = NovoPedido();
        p.ConfirmarPagamento(Agora);
        Should.Throw<DomainException>(() => p.Entregar(Usuario, Agora)).Codigo.ShouldBe("TRANSICAO_INVALIDA");
    }

    [Theory]
    [InlineData(StatusPedido.AguardandoPagamento, StatusPedido.Pago, true)]
    [InlineData(StatusPedido.AguardandoPagamento, StatusPedido.Expirado, true)]
    [InlineData(StatusPedido.Pago, StatusPedido.Cancelado, true)]
    [InlineData(StatusPedido.EmPreparo, StatusPedido.Cancelado, true)]
    [InlineData(StatusPedido.Pronto, StatusPedido.Cancelado, false)]
    [InlineData(StatusPedido.Entregue, StatusPedido.Cancelado, false)]
    [InlineData(StatusPedido.Pago, StatusPedido.Expirado, false)]
    [InlineData(StatusPedido.Cancelado, StatusPedido.Pago, false)]
    public void Tabela_de_transicoes(StatusPedido de, StatusPedido para, bool permitido) =>
        Pedido.TransicaoPermitida(de, para).ShouldBe(permitido);

    [Fact]
    public void Cancelamento_exige_motivo()
    {
        Should.Throw<DomainException>(() => NovoPedido().Cancelar("  ", Usuario, false, Agora)).Codigo.ShouldBe("CAMPO_OBRIGATORIO");
    }

    [Fact]
    public void Cancelamento_em_preparo_exige_gerencia()
    {
        var p = NovoPedido();
        p.ConfirmarPagamento(Agora);
        p.IniciarPreparo(Usuario, Agora);

        Should.Throw<DomainException>(() => p.Cancelar("Cliente desistiu", Usuario, autorizadoPelaGerencia: false, Agora))
            .Codigo.ShouldBe("CANCELAMENTO_EXIGE_GERENCIA");

        p.Cancelar("Cliente desistiu", Usuario, autorizadoPelaGerencia: true, Agora);
        p.Status.ShouldBe(StatusPedido.Cancelado);
        p.MotivoCancelamento.ShouldBe("Cliente desistiu");
    }

    [Fact]
    public void Expirar_so_apos_o_prazo()
    {
        var p = NovoPedido();
        Should.Throw<DomainException>(() => p.Expirar(Agora.AddMinutes(10))).Codigo.ShouldBe("PEDIDO_NAO_EXPIRADO");

        p.Expirar(Agora.AddMinutes(16));
        p.Status.ShouldBe(StatusPedido.Expirado);
    }

    [Fact]
    public void Precificacao_inconsistente_com_os_itens_e_bug_de_programacao()
    {
        var itens = new[] { new ItemNovoPedido(Guid.NewGuid(), "A", 10, 1) };
        Should.Throw<InvalidOperationException>(() => Pedido.Criar(Guid.NewGuid(), null, CanalPedido.Totem, Usuario, "k", itens,
            CalculadoraPedido.Calcular(99, 0, 0, 0, 0), null, null, null, Agora, TimeSpan.FromMinutes(15)));
    }
}
