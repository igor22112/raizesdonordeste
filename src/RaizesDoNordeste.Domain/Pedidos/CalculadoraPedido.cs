using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;

namespace RaizesDoNordeste.Domain.Pedidos;

public sealed record ResultadoPrecificacao(
    decimal Subtotal,
    decimal DescontoNivel,
    decimal DescontoCampanha,
    decimal DescontoPontos,
    int PontosUtilizados,
    decimal Total);

/// <summary>
/// Regras de preço do pedido (ver ADR-0005):
/// 1. Desconto de nível e desconto de campanha NÃO são cumulativos: vale o maior percentual.
/// 2. Pontos são cumulativos com o percentual.
/// 3. A soma dos descontos é limitada a 50% do subtotal; os pontos são ajustados ao teto
///    e só são debitados os pontos efetivamente usados.
/// </summary>
public static class CalculadoraPedido
{
    public static ResultadoPrecificacao Calcular(
        decimal subtotal,
        decimal percentualNivel,
        decimal percentualCampanha,
        int pontosSolicitados,
        int saldoPontos)
    {
        if (subtotal <= 0) throw new DomainException("PEDIDO_VAZIO", "O subtotal do pedido deve ser maior que zero.");
        if (pontosSolicitados < 0) throw new DomainException("PONTOS_INVALIDOS", "A quantidade de pontos não pode ser negativa.");
        if (pontosSolicitados > saldoPontos)
            throw new DomainException("PONTOS_INSUFICIENTES", $"Saldo de pontos insuficiente (saldo: {saldoPontos}).");
        if (percentualNivel is < 0 or > 1 || percentualCampanha is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(percentualNivel), "Percentuais devem estar entre 0 e 1.");

        var usarCampanha = percentualCampanha > percentualNivel;
        var percentual = usarCampanha ? percentualCampanha : percentualNivel;
        var teto = Arredondar(subtotal * RegrasFidelidade.TetoDescontoTotal);
        var descontoPercentual = Math.Min(Arredondar(subtotal * percentual), teto);

        var espacoParaPontos = teto - descontoPercentual;
        var maxPontosNoTeto = (int)Math.Floor(espacoParaPontos / RegrasFidelidade.ValorPorPonto);
        var pontosUtilizados = Math.Min(pontosSolicitados, maxPontosNoTeto);
        var descontoPontos = pontosUtilizados * RegrasFidelidade.ValorPorPonto;

        var total = subtotal - descontoPercentual - descontoPontos;

        return new ResultadoPrecificacao(
            Subtotal: subtotal,
            DescontoNivel: usarCampanha ? 0 : descontoPercentual,
            DescontoCampanha: usarCampanha ? descontoPercentual : 0,
            DescontoPontos: descontoPontos,
            PontosUtilizados: pontosUtilizados,
            Total: total);
    }

    private static decimal Arredondar(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
}
