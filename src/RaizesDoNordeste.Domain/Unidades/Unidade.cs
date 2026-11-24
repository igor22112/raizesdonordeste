using RaizesDoNordeste.Domain.Comum;

namespace RaizesDoNordeste.Domain.Unidades;

public enum Regiao { Norte, Nordeste, CentroOeste, Sudeste, Sul }

/// <summary>
/// Unidades com cozinha reduzida não vendem produtos que exigem cozinha completa.
/// </summary>
public enum TipoUnidade { CozinhaCompleta, CozinhaReduzida }

public sealed class Unidade : Entidade
{
    private Unidade() { }

    public Unidade(string nome, string cidade, string uf, Regiao regiao, TipoUnidade tipo,
        string fusoHorario, TimeOnly horaAbertura, TimeOnly horaFechamento)
    {
        Atualizar(nome, cidade, uf, regiao, tipo, fusoHorario, horaAbertura, horaFechamento, ativa: true);
    }

    public string Nome { get; private set; } = null!;
    public string Cidade { get; private set; } = null!;
    public string Uf { get; private set; } = null!;
    public Regiao Regiao { get; private set; }
    public TipoUnidade Tipo { get; private set; }

    /// <summary>Fuso IANA (ex.: America/Recife). O Brasil tem mais de um fuso.</summary>
    public string FusoHorario { get; private set; } = null!;

    /// <summary>Se abertura == fechamento, a unidade funciona 24h.</summary>
    public TimeOnly HoraAbertura { get; private set; }
    public TimeOnly HoraFechamento { get; private set; }
    public bool Ativa { get; private set; }

    public void Atualizar(string nome, string cidade, string uf, Regiao regiao, TipoUnidade tipo,
        string fusoHorario, TimeOnly horaAbertura, TimeOnly horaFechamento, bool ativa)
    {
        Nome = Guarda.Texto(nome, "Nome", 120);
        Cidade = Guarda.Texto(cidade, "Cidade", 80);
        Uf = Guarda.Texto(uf, "UF", 2).ToUpperInvariant();
        if (Uf.Length != 2) throw new DomainException("CAMPO_INVALIDO", "UF deve ter 2 letras.");
        Regiao = regiao;
        Tipo = tipo;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(fusoHorario, out _))
            throw new DomainException("FUSO_INVALIDO", $"Fuso horário '{fusoHorario}' não reconhecido.");
        FusoHorario = fusoHorario;
        HoraAbertura = horaAbertura;
        HoraFechamento = horaFechamento;
        Ativa = ativa;
    }

    public DateTimeOffset HorarioLocal(DateTimeOffset agoraUtc)
        => TimeZoneInfo.ConvertTime(agoraUtc, TimeZoneInfo.FindSystemTimeZoneById(FusoHorario));

    public DateOnly DataLocal(DateTimeOffset agoraUtc) => DateOnly.FromDateTime(HorarioLocal(agoraUtc).DateTime);

    public bool EstaAberta(DateTimeOffset agoraUtc)
    {
        if (!Ativa) return false;
        if (HoraAbertura == HoraFechamento) return true;

        var agora = TimeOnly.FromDateTime(HorarioLocal(agoraUtc).DateTime);
        return HoraAbertura < HoraFechamento
            ? agora >= HoraAbertura && agora < HoraFechamento
            : agora >= HoraAbertura || agora < HoraFechamento; // expediente que vira a meia-noite
    }
}
