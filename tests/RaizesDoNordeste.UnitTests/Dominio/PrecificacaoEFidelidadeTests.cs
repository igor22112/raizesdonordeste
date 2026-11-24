using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.UnitTests.Dominio;

public class CalculadoraPedidoTests
{
    [Fact]
    public void Sem_descontos_total_igual_subtotal()
    {
        var r = CalculadoraPedido.Calcular(50m, 0, 0, 0, 0);
        r.Total.ShouldBe(50m);
        r.PontosUtilizados.ShouldBe(0);
    }

    [Fact]
    public void Desconto_de_nivel_e_aplicado()
    {
        var r = CalculadoraPedido.Calcular(100m, 0.05m, 0, 0, 0);
        r.DescontoNivel.ShouldBe(5m);
        r.DescontoCampanha.ShouldBe(0m);
        r.Total.ShouldBe(95m);
    }

    [Fact]
    public void Nivel_e_campanha_nao_acumulam_vale_o_maior()
    {
        var r = CalculadoraPedido.Calcular(100m, 0.05m, 0.10m, 0, 0);
        r.DescontoNivel.ShouldBe(0m);
        r.DescontoCampanha.ShouldBe(10m);
        r.Total.ShouldBe(90m);

        var r2 = CalculadoraPedido.Calcular(100m, 0.05m, 0.03m, 0, 0);
        r2.DescontoNivel.ShouldBe(5m);
        r2.DescontoCampanha.ShouldBe(0m);
    }

    [Fact]
    public void Pontos_acumulam_com_percentual()
    {
        // 100 pontos = R$ 5,00
        var r = CalculadoraPedido.Calcular(100m, 0.05m, 0, 100, 500);
        r.DescontoNivel.ShouldBe(5m);
        r.DescontoPontos.ShouldBe(5m);
        r.PontosUtilizados.ShouldBe(100);
        r.Total.ShouldBe(90m);
    }

    [Fact]
    public void Descontos_limitados_a_50_por_cento_e_so_debita_pontos_usados()
    {
        // subtotal 20 -> teto 10. Nível 5% = 1,00. Sobram 9,00 = 180 pontos.
        var r = CalculadoraPedido.Calcular(20m, 0.05m, 0, 1000, 1000);
        r.DescontoNivel.ShouldBe(1m);
        r.PontosUtilizados.ShouldBe(180);
        r.DescontoPontos.ShouldBe(9m);
        r.Total.ShouldBe(10m);
    }

    [Fact]
    public void Pontos_acima_do_saldo_sao_rejeitados() =>
        Should.Throw<DomainException>(() => CalculadoraPedido.Calcular(50m, 0, 0, 200, 100)).Codigo.ShouldBe("PONTOS_INSUFICIENTES");

    [Fact]
    public void Subtotal_zero_e_rejeitado() =>
        Should.Throw<DomainException>(() => CalculadoraPedido.Calcular(0m, 0, 0, 0, 0)).Codigo.ShouldBe("PEDIDO_VAZIO");

    [Fact]
    public void Arredondamento_em_centavos()
    {
        var r = CalculadoraPedido.Calcular(37.80m, 0.05m, 0, 0, 0);
        r.DescontoNivel.ShouldBe(1.89m);
        r.Total.ShouldBe(35.91m);
    }
}

public class RegrasFidelidadeTests
{
    [Theory]
    [InlineData(0, NivelFidelidade.Bronze)]
    [InlineData(4, NivelFidelidade.Bronze)]
    [InlineData(5, NivelFidelidade.Prata)]
    [InlineData(14, NivelFidelidade.Prata)]
    [InlineData(15, NivelFidelidade.Ouro)]
    [InlineData(200, NivelFidelidade.Ouro)]
    public void Nivel_por_frequencia(int pedidos, NivelFidelidade esperado) =>
        RegrasFidelidade.CalcularNivel(pedidos).ShouldBe(esperado);

    [Theory]
    [InlineData(NivelFidelidade.Bronze, 0)]
    [InlineData(NivelFidelidade.Prata, 0.03)]
    [InlineData(NivelFidelidade.Ouro, 0.05)]
    public void Desconto_progressivo(NivelFidelidade nivel, decimal percentual) =>
        RegrasFidelidade.PercentualDesconto(nivel).ShouldBe(percentual);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9.99, 9)]
    [InlineData(30.91, 30)]
    [InlineData(-5, 0)]
    public void Pontos_ganhos_um_por_real_arredondado_para_baixo(decimal valor, int pontos) =>
        RegrasFidelidade.PontosGanhos(valor).ShouldBe(pontos);

    [Fact]
    public void Movimentacao_com_pontos_nao_positivos_e_rejeitada() =>
        Should.Throw<DomainException>(() => new MovimentacaoPontos(Guid.NewGuid(), TipoMovimentacaoPontos.Credito, 0, null, DateTimeOffset.UtcNow));

    [Fact]
    public void Anonimizar_remove_dados_pessoais()
    {
        var c = new Cliente(Guid.NewGuid(), new DateOnly(1990, 1, 1), "81999999999");
        c.Anonimizar(DateTimeOffset.UtcNow);
        c.DataNascimento.ShouldBeNull();
        c.Telefone.ShouldBeNull();
        c.Anonimizado.ShouldBeTrue();
    }
}
