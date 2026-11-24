using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Unidades;

namespace RaizesDoNordeste.Domain.Campanhas;

/// <summary>
/// Campanha promocional com segmentação opcional por frequência, nível e região.
/// Campanhas sem segmentação valem para qualquer cliente identificado. Campanhas segmentadas
/// por frequência ou nível usam dados de perfil e, por isso, exigem o consentimento
/// CampanhasPersonalizadas (LGPD).
/// </summary>
public sealed class Campanha : Entidade
{
    private Campanha() { }

    public Campanha(string nome, string? descricao, decimal percentualDesconto, DateTimeOffset inicioEm, DateTimeOffset fimEm,
        Segmentacao segmentacao)
    {
        Nome = Guarda.Texto(nome, "Nome", 120);
        Descricao = Guarda.TextoOpcional(descricao, "Descrição", 500);
        if (percentualDesconto is <= 0 or > 0.30m)
            throw new DomainException("PERCENTUAL_INVALIDO", "O desconto da campanha deve estar entre 1% e 30%.");
        PercentualDesconto = percentualDesconto;
        if (fimEm <= inicioEm)
            throw new DomainException("PERIODO_INVALIDO", "O fim da campanha deve ser posterior ao início.");
        InicioEm = inicioEm;
        FimEm = fimEm;
        segmentacao.Validar();
        FrequenciaMinima = segmentacao.FrequenciaMinima;
        NivelMinimo = segmentacao.NivelMinimo;
        Regiao = segmentacao.Regiao;
        Ativa = true;
    }

    public string Nome { get; private set; } = null!;
    public string? Descricao { get; private set; }

    /// <summary>Fração (0,10 = 10%).</summary>
    public decimal PercentualDesconto { get; private set; }
    public DateTimeOffset InicioEm { get; private set; }
    public DateTimeOffset FimEm { get; private set; }
    public bool Ativa { get; private set; }

    public int? FrequenciaMinima { get; private set; }
    public NivelFidelidade? NivelMinimo { get; private set; }
    public Regiao? Regiao { get; private set; }

    public Segmentacao Segmentacao => new(FrequenciaMinima, NivelMinimo, Regiao);

    public bool EstaVigente(DateTimeOffset agora) => Ativa && agora >= InicioEm && agora <= FimEm;

    public void Desativar() => Ativa = false;
}

public sealed record Segmentacao(
    int? FrequenciaMinima = null,
    NivelFidelidade? NivelMinimo = null,
    Regiao? Regiao = null)
{
    /// <summary>Usa dados de perfil do cliente (frequência ou nível)?</summary>
    public bool UsaPerfil => FrequenciaMinima is not null || NivelMinimo is not null;

    public void Validar()
    {
        if (FrequenciaMinima is < 1)
            throw new DomainException("SEGMENTACAO_INVALIDA", "Frequência mínima deve ser maior que zero.");
    }
}

/// <summary>Dados do cliente usados na avaliação de elegibilidade.</summary>
public sealed record PerfilCliente(
    int PedidosEntreguesNaJanela,
    NivelFidelidade Nivel,
    bool ConsentiuCampanhasPersonalizadas);

public sealed record ResultadoElegibilidade(bool Elegivel, string? Motivo)
{
    public static readonly ResultadoElegibilidade Sim = new(true, null);
    public static ResultadoElegibilidade Nao(string motivo) => new(false, motivo);
}

public static class ElegibilidadeCampanha
{
    public static ResultadoElegibilidade Avaliar(Campanha campanha, PerfilCliente perfil, Regiao? regiaoDaUnidade, DateTimeOffset agora)
    {
        if (!campanha.EstaVigente(agora))
            return ResultadoElegibilidade.Nao("Campanha fora do período de vigência.");

        var seg = campanha.Segmentacao;

        if (seg.UsaPerfil && !perfil.ConsentiuCampanhasPersonalizadas)
            return ResultadoElegibilidade.Nao("Campanha segmentada exige consentimento para campanhas personalizadas.");

        if (seg.Regiao is { } regiao && regiaoDaUnidade is { } r && r != regiao)
            return ResultadoElegibilidade.Nao("Campanha não válida para a região desta unidade.");

        if (seg.FrequenciaMinima is { } freq && perfil.PedidosEntreguesNaJanela < freq)
            return ResultadoElegibilidade.Nao($"Exige ao menos {freq} pedidos nos últimos {RegrasFidelidade.JanelaFrequenciaDias} dias.");

        if (seg.NivelMinimo is { } nivel && perfil.Nivel < nivel)
            return ResultadoElegibilidade.Nao($"Exige nível {nivel} ou superior.");

        return ResultadoElegibilidade.Sim;
    }
}
