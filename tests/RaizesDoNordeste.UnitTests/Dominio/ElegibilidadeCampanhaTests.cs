using RaizesDoNordeste.Domain.Campanhas;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Unidades;

namespace RaizesDoNordeste.UnitTests.Dominio;

public class ElegibilidadeCampanhaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static Campanha Campanha(Segmentacao seg) =>
        new("Semana do Cuscuz", null, 0.10m, Agora.AddDays(-1), Agora.AddDays(7), seg);

    private static PerfilCliente Perfil(int pedidos = 0, bool consentiu = true) =>
        new(pedidos, RegrasFidelidade.CalcularNivel(pedidos), consentiu);

    [Fact]
    public void Campanha_sem_segmentacao_vale_para_qualquer_cliente_mesmo_sem_consentimento() =>
        ElegibilidadeCampanha.Avaliar(Campanha(new()), Perfil(consentiu: false), null, Agora).Elegivel.ShouldBeTrue();

    [Fact]
    public void Fora_da_vigencia_nao_e_elegivel() =>
        ElegibilidadeCampanha.Avaliar(Campanha(new()), Perfil(), null, Agora.AddDays(30)).Elegivel.ShouldBeFalse();

    [Fact]
    public void Campanha_desativada_nao_e_elegivel()
    {
        var c = Campanha(new());
        c.Desativar();
        ElegibilidadeCampanha.Avaliar(c, Perfil(), null, Agora).Elegivel.ShouldBeFalse();
    }

    [Fact]
    public void Segmentacao_por_perfil_exige_consentimento()
    {
        var r = ElegibilidadeCampanha.Avaliar(Campanha(new(FrequenciaMinima: 1)), Perfil(pedidos: 10, consentiu: false), null, Agora);
        r.Elegivel.ShouldBeFalse();
        r.Motivo!.ShouldContain("consentimento");
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Frequencia_minima(int pedidos, bool elegivel) =>
        ElegibilidadeCampanha.Avaliar(Campanha(new(FrequenciaMinima: 3)), Perfil(pedidos), null, Agora).Elegivel.ShouldBe(elegivel);

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(20, true)]
    public void Nivel_minimo(int pedidos, bool elegivel) =>
        ElegibilidadeCampanha.Avaliar(Campanha(new(NivelMinimo: NivelFidelidade.Prata)), Perfil(pedidos), null, Agora).Elegivel.ShouldBe(elegivel);

    [Fact]
    public void Regiao_diferente_nao_e_elegivel()
    {
        var c = Campanha(new(Regiao: Regiao.Nordeste));
        ElegibilidadeCampanha.Avaliar(c, Perfil(), Regiao.Sudeste, Agora).Elegivel.ShouldBeFalse();
        ElegibilidadeCampanha.Avaliar(c, Perfil(), Regiao.Nordeste, Agora).Elegivel.ShouldBeTrue();
    }

    [Fact]
    public void Percentual_acima_de_30_e_rejeitado() =>
        Should.Throw<DomainException>(() => new Campanha("X", null, 0.5m, Agora, Agora.AddDays(1), new())).Codigo.ShouldBe("PERCENTUAL_INVALIDO");
}
