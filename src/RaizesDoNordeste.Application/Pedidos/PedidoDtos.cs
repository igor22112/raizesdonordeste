using FluentValidation;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.Application.Pedidos;

public sealed record ItemPedidoRequest(Guid ProdutoId, int Quantidade);

/// <param name="CanalPedido">Obrigatório. Deve ser compatível com o perfil: Cliente → App, Web ou Pickup; Totem → Totem; Atendente/Gerente → Balcao.</param>
public sealed record CriarPedidoRequest(
    Guid UnidadeId,
    IReadOnlyList<ItemPedidoRequest> Itens,
    CanalPedido? CanalPedido = null,
    int PontosResgatar = 0,
    Guid? CampanhaId = null,
    string? NomeChamada = null,
    string? Observacao = null);

public sealed record AtualizarStatusPedidoRequest(StatusPedido Status);

public sealed record CancelarPedidoRequest(string Motivo);

public sealed record FiltroPedidos(Guid? UnidadeId, StatusPedido? Status, CanalPedido? CanalPedido, DateTimeOffset? De, DateTimeOffset? Ate, int? Pagina, int? Tamanho);

public sealed record PedidoItemDto(Guid ProdutoId, string NomeProduto, decimal PrecoUnitario, int Quantidade, decimal Subtotal);

public sealed record HistoricoStatusDto(StatusPedido? De, StatusPedido Para, Guid? UsuarioId, string? Motivo, DateTimeOffset OcorridoEm);

public sealed record PagamentoDto(
    Guid Id, Guid PedidoId, MetodoPagamento Metodo, StatusPagamento Status, decimal Valor, string? Detalhe,
    DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm);

public sealed record PedidoDto(
    Guid Id,
    Guid UnidadeId,
    Guid? ClienteId,
    CanalPedido CanalPedido,
    StatusPedido Status,
    string? NomeChamada,
    string? Observacao,
    decimal Subtotal,
    decimal DescontoNivel,
    decimal DescontoCampanha,
    decimal DescontoPontos,
    decimal Total,
    int PontosResgatados,
    Guid? CampanhaId,
    string? MotivoCancelamento,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    DateTimeOffset ExpiraEm,
    IReadOnlyList<PedidoItemDto> Itens,
    IReadOnlyList<HistoricoStatusDto> Historico,
    IReadOnlyList<PagamentoDto> Pagamentos);

public sealed record PedidoResumoDto(
    Guid Id, Guid UnidadeId, CanalPedido CanalPedido, StatusPedido Status, string? NomeChamada, decimal Total, int QuantidadeItens,
    DateTimeOffset CriadoEm);

public sealed record ResultadoCriacaoPedido(PedidoDto Pedido, bool Criado);

public sealed class CriarPedidoValidator : AbstractValidator<CriarPedidoRequest>
{
    public CriarPedidoValidator()
    {
        RuleFor(x => x.UnidadeId).NotEmpty();
        RuleFor(x => x.Itens).NotEmpty().WithMessage("O pedido deve ter ao menos um item.");
        RuleFor(x => x.Itens.Count).LessThanOrEqualTo(Pedido.MaxItens).When(x => x.Itens is not null);
        RuleForEach(x => x.Itens).ChildRules(i =>
        {
            i.RuleFor(x => x.ProdutoId).NotEmpty();
            i.RuleFor(x => x.Quantidade).InclusiveBetween(1, Pedido.MaxQuantidadePorItem);
        });
        RuleFor(x => x.Itens).Must(i => i.Select(x => x.ProdutoId).Distinct().Count() == i.Count)
            .When(x => x.Itens is not null)
            .WithMessage("Cada produto deve aparecer uma única vez; use a quantidade.");
        RuleFor(x => x.CanalPedido).NotNull().WithMessage("canalPedido é obrigatório (App, Web, Totem, Balcao ou Pickup).")
            .IsInEnum().WithMessage("canalPedido inválido.");
        RuleFor(x => x.PontosResgatar).InclusiveBetween(0, 100_000);
        RuleFor(x => x.NomeChamada).MaximumLength(40);
        RuleFor(x => x.Observacao).MaximumLength(200);
    }
}

public sealed class CancelarPedidoValidator : AbstractValidator<CancelarPedidoRequest>
{
    public CancelarPedidoValidator()
    {
        RuleFor(x => x.Motivo).NotEmpty().MinimumLength(3).MaximumLength(300);
    }
}
