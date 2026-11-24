using RaizesDoNordeste.Domain.Comum;

namespace RaizesDoNordeste.Domain.Estoques;

public enum TipoMovimentacaoEstoque
{
    Entrada,
    Ajuste,
    Perda,
    SaidaVenda,
    DevolucaoCancelamento
}

/// <summary>
/// Saldo de um produto em uma unidade.
/// Quantidade = saldo físico; Reservada = comprometida com pedidos aguardando pagamento.
/// As operações de reserva/baixa em pedidos são feitas com UPDATE atômico condicional
/// no banco (ver EstoqueOperacoes), para não vender acima do saldo em horário de pico.
/// As operações manuais (entrada, ajuste, perda) usam estes métodos + controle de concorrência otimista.
/// </summary>
public sealed class ItemEstoque
{
    private ItemEstoque() { }

    public ItemEstoque(Guid unidadeId, Guid produtoId, int minimo = 0)
    {
        UnidadeId = unidadeId;
        ProdutoId = produtoId;
        DefinirMinimo(minimo);
    }

    public Guid UnidadeId { get; private set; }
    public Guid ProdutoId { get; private set; }
    public int Quantidade { get; private set; }
    public int Reservada { get; private set; }
    public int Minimo { get; private set; }
    public byte[] Versao { get; private set; } = [];

    public int Disponivel => Quantidade - Reservada;
    public bool AbaixoDoMinimo => Disponivel <= Minimo;

    public void Entrada(int quantidade)
    {
        if (quantidade <= 0) throw new DomainException("QUANTIDADE_INVALIDA", "A entrada deve ser maior que zero.");
        Quantidade += quantidade;
    }

    /// <summary>Ajuste de inventário (positivo ou negativo). Não pode deixar o saldo abaixo do reservado.</summary>
    public void Ajustar(int delta)
    {
        if (delta == 0) throw new DomainException("QUANTIDADE_INVALIDA", "O ajuste não pode ser zero.");
        var novo = Quantidade + delta;
        if (novo < Reservada)
            throw new DomainException("AJUSTE_INVALIDO", $"O saldo não pode ficar abaixo do reservado para pedidos ({Reservada}).");
        Quantidade = novo;
    }

    public void RegistrarPerda(int quantidade)
    {
        if (quantidade <= 0) throw new DomainException("QUANTIDADE_INVALIDA", "A perda deve ser maior que zero.");
        Ajustar(-quantidade);
    }

    public void DefinirMinimo(int minimo)
    {
        if (minimo < 0) throw new DomainException("QUANTIDADE_INVALIDA", "O estoque mínimo não pode ser negativo.");
        Minimo = minimo;
    }
}

public sealed class MovimentacaoEstoque : Entidade
{
    private MovimentacaoEstoque() { }

    public MovimentacaoEstoque(Guid unidadeId, Guid produtoId, TipoMovimentacaoEstoque tipo, int quantidade,
        string? motivo, Guid? usuarioId, Guid? pedidoId, DateTimeOffset ocorridoEm)
    {
        UnidadeId = unidadeId;
        ProdutoId = produtoId;
        Tipo = tipo;
        Quantidade = quantidade;
        Motivo = Guarda.TextoOpcional(motivo, "Motivo", 300);
        UsuarioId = usuarioId;
        PedidoId = pedidoId;
        OcorridoEm = ocorridoEm;
    }

    public Guid UnidadeId { get; private set; }
    public Guid ProdutoId { get; private set; }
    public TipoMovimentacaoEstoque Tipo { get; private set; }

    /// <summary>Quantidade com sinal: positiva aumenta o saldo físico, negativa diminui.</summary>
    public int Quantidade { get; private set; }
    public string? Motivo { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public Guid? PedidoId { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }
}
