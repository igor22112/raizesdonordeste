using System.Globalization;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Unidades;

namespace RaizesDoNordeste.Domain.Catalogo;

public enum CategoriaProduto { Tapioca, Cuscuz, Bolo, Salgado, Bebida, CafeDaManha, Doce, Combo }

/// <summary>
/// Período do ano (recorrente) em que um produto sazonal pode ser vendido.
/// Ex.: produtos juninos de 01/06 a 31/07. Suporta janelas que viram o ano (ex.: 01/12 a 31/01).
/// Armazenado como MMDD (inteiro) para facilitar comparação.
/// </summary>
public sealed record JanelaSazonal
{
    public JanelaSazonal(int inicioMmdd, int fimMmdd)
    {
        Validar(inicioMmdd);
        Validar(fimMmdd);
        InicioMmdd = inicioMmdd;
        FimMmdd = fimMmdd;
    }

    public int InicioMmdd { get; }
    public int FimMmdd { get; }

    public bool Contem(DateOnly data)
    {
        var valor = data.Month * 100 + data.Day;
        return InicioMmdd <= FimMmdd
            ? valor >= InicioMmdd && valor <= FimMmdd
            : valor >= InicioMmdd || valor <= FimMmdd;
    }

    /// <summary>Converte "MM-dd" (ex.: "06-01") em MMDD.</summary>
    public static int ParseMesDia(string texto)
    {
        if (!DateTime.TryParseExact($"2024-{texto}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw new DomainException("SAZONALIDADE_INVALIDA", $"Data '{texto}' inválida. Use o formato MM-dd.");
        return d.Month * 100 + d.Day;
    }

    public static string FormatarMesDia(int mmdd) => $"{mmdd / 100:00}-{mmdd % 100:00}";

    private static void Validar(int mmdd)
    {
        var mes = mmdd / 100;
        var dia = mmdd % 100;
        if (mes is < 1 or > 12 || dia < 1 || dia > DateTime.DaysInMonth(2024, mes))
            throw new DomainException("SAZONALIDADE_INVALIDA", $"Data sazonal {mmdd} inválida.");
    }
}

public sealed class Produto : Entidade
{
    private Produto() { }

    public Produto(string nome, string? descricao, CategoriaProduto categoria, decimal precoBase,
        bool requerCozinhaCompleta, JanelaSazonal? sazonalidade)
    {
        Atualizar(nome, descricao, categoria, precoBase, requerCozinhaCompleta, sazonalidade, ativo: true);
    }

    public string Nome { get; private set; } = null!;
    public string? Descricao { get; private set; }
    public CategoriaProduto Categoria { get; private set; }
    public decimal PrecoBase { get; private set; }
    public bool RequerCozinhaCompleta { get; private set; }
    public int? SazonalInicioMmdd { get; private set; }
    public int? SazonalFimMmdd { get; private set; }
    public bool Ativo { get; private set; }

    public JanelaSazonal? Sazonalidade =>
        SazonalInicioMmdd is { } i && SazonalFimMmdd is { } f ? new JanelaSazonal(i, f) : null;

    public void Atualizar(string nome, string? descricao, CategoriaProduto categoria, decimal precoBase,
        bool requerCozinhaCompleta, JanelaSazonal? sazonalidade, bool ativo)
    {
        Nome = Guarda.Texto(nome, "Nome", 120);
        Descricao = Guarda.TextoOpcional(descricao, "Descrição", 500);
        Categoria = categoria;
        PrecoBase = Guarda.Dinheiro(precoBase, "Preço base");
        RequerCozinhaCompleta = requerCozinhaCompleta;
        SazonalInicioMmdd = sazonalidade?.InicioMmdd;
        SazonalFimMmdd = sazonalidade?.FimMmdd;
        Ativo = ativo;
    }

    public bool DisponivelNaData(DateOnly dataLocal) => Sazonalidade?.Contem(dataLocal) ?? true;

    public bool CompativelCom(TipoUnidade tipo) => !RequerCozinhaCompleta || tipo == TipoUnidade.CozinhaCompleta;
}

/// <summary>
/// Produto no cardápio de uma unidade: disponibilidade local, preço local opcional
/// (sobrescreve o preço base) e observação de variação regional da receita.
/// </summary>
public sealed class ItemCardapio
{
    private ItemCardapio() { }

    public ItemCardapio(Guid unidadeId, Guid produtoId)
    {
        UnidadeId = unidadeId;
        ProdutoId = produtoId;
        Disponivel = true;
    }

    public Guid UnidadeId { get; private set; }
    public Guid ProdutoId { get; private set; }
    public Produto Produto { get; private set; } = null!;
    public decimal? PrecoLocal { get; private set; }
    public bool Disponivel { get; private set; }
    public string? ObservacaoRegional { get; private set; }

    public void Configurar(bool disponivel, decimal? precoLocal, string? observacaoRegional)
    {
        Disponivel = disponivel;
        PrecoLocal = precoLocal is null ? null : Guarda.Dinheiro(precoLocal.Value, "Preço local");
        ObservacaoRegional = Guarda.TextoOpcional(observacaoRegional, "Observação regional", 300);
    }

    public decimal PrecoEfetivo(Produto produto) => PrecoLocal ?? produto.PrecoBase;
}
