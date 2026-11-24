using RaizesDoNordeste.Domain.Comum;

namespace RaizesDoNordeste.Domain.Pedidos;

/// <summary>Canal de origem do pedido (multicanalidade, §2 do estudo de caso). Dado de domínio obrigatório.</summary>
public enum CanalPedido { App, Web, Totem, Balcao, Pickup }

public enum StatusPedido
{
    AguardandoPagamento,
    Pago,
    EmPreparo,
    Pronto,
    Entregue,
    Cancelado,
    Expirado
}

public sealed record PedidoCriado(Guid PedidoId, Guid UnidadeId, CanalPedido CanalPedido, decimal Total, DateTimeOffset OcorridoEm) : IEventoDominio;

public sealed record PedidoStatusAlterado(Guid PedidoId, Guid UnidadeId, Guid? ClienteId, StatusPedido De, StatusPedido Para, DateTimeOffset OcorridoEm) : IEventoDominio;

public sealed record ItemNovoPedido(Guid ProdutoId, string NomeProduto, decimal PrecoUnitario, int Quantidade);

/// <summary>
/// Agregado Pedido. Concentra a máquina de estados: toda mudança de status passa por
/// <see cref="MudarStatus"/>, que valida a transição, grava o histórico e emite evento.
/// </summary>
public sealed class Pedido : RaizAgregado
{
    public const int MaxItens = 30;
    public const int MaxQuantidadePorItem = 20;

    private static readonly Dictionary<StatusPedido, StatusPedido[]> Transicoes = new()
    {
        [StatusPedido.AguardandoPagamento] = [StatusPedido.Pago, StatusPedido.Cancelado, StatusPedido.Expirado],
        [StatusPedido.Pago] = [StatusPedido.EmPreparo, StatusPedido.Cancelado],
        [StatusPedido.EmPreparo] = [StatusPedido.Pronto, StatusPedido.Cancelado],
        [StatusPedido.Pronto] = [StatusPedido.Entregue],
        [StatusPedido.Entregue] = [],
        [StatusPedido.Cancelado] = [],
        [StatusPedido.Expirado] = []
    };

    private readonly List<PedidoItem> _itens = [];
    private readonly List<HistoricoStatusPedido> _historico = [];

    private Pedido() { }

    public Guid UnidadeId { get; private set; }
    public Guid? ClienteId { get; private set; }
    public Guid CriadoPorUsuarioId { get; private set; }
    public string ChaveIdempotencia { get; private set; } = null!;
    public CanalPedido CanalPedido { get; private set; }
    public StatusPedido Status { get; private set; }
    public string? NomeChamada { get; private set; }
    public string? Observacao { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DescontoNivel { get; private set; }
    public decimal DescontoCampanha { get; private set; }
    public decimal DescontoPontos { get; private set; }
    public decimal Total { get; private set; }
    public int PontosResgatados { get; private set; }
    public Guid? CampanhaId { get; private set; }
    public string? MotivoCancelamento { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <summary>Prazo para pagamento; depois disso o pedido expira e libera o estoque reservado.</summary>
    public DateTimeOffset ExpiraEm { get; private set; }
    public byte[] Versao { get; private set; } = [];

    public IReadOnlyCollection<PedidoItem> Itens => _itens;
    public IReadOnlyCollection<HistoricoStatusPedido> Historico => _historico;

    public decimal DescontoTotal => DescontoNivel + DescontoCampanha + DescontoPontos;

    public static Pedido Criar(
        Guid unidadeId, Guid? clienteId, CanalPedido canal, Guid criadoPorUsuarioId, string chaveIdempotencia,
        IReadOnlyCollection<ItemNovoPedido> itens, ResultadoPrecificacao precificacao, Guid? campanhaId,
        string? nomeChamada, string? observacao, DateTimeOffset agora, TimeSpan prazoPagamento)
    {
        if (canal is CanalPedido.App or CanalPedido.Web or CanalPedido.Pickup && clienteId is null)
            throw new DomainException("CLIENTE_OBRIGATORIO", "Pedidos pelo app, web ou para retirada (pickup) exigem cliente identificado.");
        if (itens.Count == 0)
            throw new DomainException("PEDIDO_VAZIO", "O pedido deve ter ao menos um item.");
        if (itens.Count > MaxItens)
            throw new DomainException("PEDIDO_MUITOS_ITENS", $"O pedido pode ter no máximo {MaxItens} itens.");
        if (itens.GroupBy(i => i.ProdutoId).Any(g => g.Count() > 1))
            throw new DomainException("ITEM_DUPLICADO", "Cada produto deve aparecer uma única vez; use a quantidade.");

        var pedido = new Pedido
        {
            UnidadeId = unidadeId,
            ClienteId = clienteId,
            CanalPedido = canal,
            CriadoPorUsuarioId = criadoPorUsuarioId,
            ChaveIdempotencia = Guarda.Texto(chaveIdempotencia, "Chave de idempotência", 100),
            NomeChamada = Guarda.TextoOpcional(nomeChamada, "Nome para chamada", 40),
            Observacao = Guarda.TextoOpcional(observacao, "Observação", 200),
            Status = StatusPedido.AguardandoPagamento,
            CriadoEm = agora,
            AtualizadoEm = agora,
            ExpiraEm = agora.Add(prazoPagamento)
        };

        foreach (var item in itens)
            pedido._itens.Add(new PedidoItem(pedido.Id, item.ProdutoId, item.NomeProduto, item.PrecoUnitario, item.Quantidade));

        var subtotal = pedido._itens.Sum(i => i.Subtotal);
        if (subtotal != precificacao.Subtotal)
            throw new InvalidOperationException("A precificação foi calculada sobre um subtotal diferente dos itens.");

        pedido.Subtotal = precificacao.Subtotal;
        pedido.DescontoNivel = precificacao.DescontoNivel;
        pedido.DescontoCampanha = precificacao.DescontoCampanha;
        pedido.DescontoPontos = precificacao.DescontoPontos;
        pedido.PontosResgatados = precificacao.PontosUtilizados;
        pedido.Total = precificacao.Total;
        pedido.CampanhaId = precificacao.DescontoCampanha > 0 ? campanhaId : null;

        pedido._historico.Add(new HistoricoStatusPedido(pedido.Id, null, StatusPedido.AguardandoPagamento, criadoPorUsuarioId, null, agora));
        pedido.AdicionarEvento(new PedidoCriado(pedido.Id, unidadeId, canal, pedido.Total, agora));
        return pedido;
    }

    public static bool TransicaoPermitida(StatusPedido de, StatusPedido para) => Transicoes[de].Contains(para);

    public bool EstaFinalizado => Transicoes[Status].Length == 0;

    public bool PrazoPagamentoVencido(DateTimeOffset agora) => Status == StatusPedido.AguardandoPagamento && agora > ExpiraEm;

    public void ConfirmarPagamento(DateTimeOffset agora) => MudarStatus(StatusPedido.Pago, null, null, agora);

    public void IniciarPreparo(Guid usuarioId, DateTimeOffset agora) => MudarStatus(StatusPedido.EmPreparo, usuarioId, null, agora);

    public void MarcarPronto(Guid usuarioId, DateTimeOffset agora) => MudarStatus(StatusPedido.Pronto, usuarioId, null, agora);

    public void Entregar(Guid usuarioId, DateTimeOffset agora) => MudarStatus(StatusPedido.Entregue, usuarioId, null, agora);

    public void Expirar(DateTimeOffset agora)
    {
        if (!PrazoPagamentoVencido(agora))
            throw new DomainException("PEDIDO_NAO_EXPIRADO", "O prazo de pagamento deste pedido ainda não venceu.");
        MudarStatus(StatusPedido.Expirado, null, "Prazo de pagamento esgotado", agora);
    }

    /// <summary>
    /// Cancela o pedido. Depois que o preparo começou, só a gerência pode cancelar
    /// (há perda de insumos), e o motivo é sempre obrigatório para auditoria.
    /// </summary>
    public void Cancelar(string motivo, Guid usuarioId, bool autorizadoPelaGerencia, DateTimeOffset agora)
    {
        var motivoValido = Guarda.Texto(motivo, "Motivo do cancelamento", 300);
        if (Status == StatusPedido.EmPreparo && !autorizadoPelaGerencia)
            throw new DomainException("CANCELAMENTO_EXIGE_GERENCIA", "Pedidos em preparo só podem ser cancelados pela gerência.");
        MudarStatus(StatusPedido.Cancelado, usuarioId, motivoValido, agora);
        MotivoCancelamento = motivoValido;
    }

    private void MudarStatus(StatusPedido novo, Guid? usuarioId, string? motivo, DateTimeOffset agora)
    {
        if (!TransicaoPermitida(Status, novo))
            throw new DomainException("TRANSICAO_INVALIDA", $"Não é possível alterar o pedido de {Status} para {novo}.");

        var anterior = Status;
        Status = novo;
        AtualizadoEm = agora;
        _historico.Add(new HistoricoStatusPedido(Id, anterior, novo, usuarioId, motivo, agora));
        AdicionarEvento(new PedidoStatusAlterado(Id, UnidadeId, ClienteId, anterior, novo, agora));
    }
}

public sealed class PedidoItem : Entidade
{
    private PedidoItem() { }

    internal PedidoItem(Guid pedidoId, Guid produtoId, string nomeProduto, decimal precoUnitario, int quantidade)
    {
        if (quantidade is < 1 or > Pedido.MaxQuantidadePorItem)
            throw new DomainException("QUANTIDADE_INVALIDA", $"A quantidade por item deve estar entre 1 e {Pedido.MaxQuantidadePorItem}.");
        PedidoId = pedidoId;
        ProdutoId = produtoId;
        NomeProduto = Guarda.Texto(nomeProduto, "Nome do produto", 120);
        PrecoUnitario = Guarda.Dinheiro(precoUnitario, "Preço unitário");
        Quantidade = quantidade;
    }

    public Guid PedidoId { get; private set; }
    public Guid ProdutoId { get; private set; }

    /// <summary>Cópia do nome e preço no momento da compra (o catálogo pode mudar depois).</summary>
    public string NomeProduto { get; private set; } = null!;
    public decimal PrecoUnitario { get; private set; }
    public int Quantidade { get; private set; }
    public decimal Subtotal => PrecoUnitario * Quantidade;
}

public sealed class HistoricoStatusPedido : Entidade
{
    private HistoricoStatusPedido() { }

    internal HistoricoStatusPedido(Guid pedidoId, StatusPedido? de, StatusPedido para, Guid? usuarioId, string? motivo, DateTimeOffset ocorridoEm)
    {
        PedidoId = pedidoId;
        De = de;
        Para = para;
        UsuarioId = usuarioId;
        Motivo = motivo;
        OcorridoEm = ocorridoEm;
    }

    public Guid PedidoId { get; private set; }
    public StatusPedido? De { get; private set; }
    public StatusPedido Para { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string? Motivo { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }
}
