namespace RaizesDoNordeste.Domain.Clientes;

public enum NivelFidelidade { Bronze, Prata, Ouro }

/// <summary>
/// Regras do programa de fidelidade (valores definidos como premissa do projeto,
/// ver ADR-0005). Centralizadas aqui para serem testáveis e fáceis de alterar.
/// </summary>
public static class RegrasFidelidade
{
    /// <summary>Pontos ganhos por real pago (arredondado para baixo).</summary>
    public const int PontosPorReal = 1;

    /// <summary>Valor de desconto de cada ponto resgatado (100 pontos = R$ 5,00).</summary>
    public const decimal ValorPorPonto = 0.05m;

    /// <summary>Soma de todos os descontos não pode passar de 50% do subtotal.</summary>
    public const decimal TetoDescontoTotal = 0.50m;

    /// <summary>Janela usada para medir frequência de consumo.</summary>
    public const int JanelaFrequenciaDias = 90;

    public const int PedidosParaPrata = 5;
    public const int PedidosParaOuro = 15;

    public static NivelFidelidade CalcularNivel(int pedidosEntreguesNaJanela) => pedidosEntreguesNaJanela switch
    {
        >= PedidosParaOuro => NivelFidelidade.Ouro,
        >= PedidosParaPrata => NivelFidelidade.Prata,
        _ => NivelFidelidade.Bronze
    };

    /// <summary>Desconto progressivo por nível.</summary>
    public static decimal PercentualDesconto(NivelFidelidade nivel) => nivel switch
    {
        NivelFidelidade.Ouro => 0.05m,
        NivelFidelidade.Prata => 0.03m,
        _ => 0m
    };

    public static int PontosGanhos(decimal valorPago) =>
        valorPago <= 0 ? 0 : (int)Math.Floor(valorPago) * PontosPorReal;
}
