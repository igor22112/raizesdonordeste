namespace RaizesDoNordeste.Domain.Comum;

/// <summary>
/// Violação de regra de negócio. O código é estável e pode ser usado pelo
/// front-end para exibir mensagens específicas (ex.: ESTOQUE_INSUFICIENTE).
/// A API converte esta exceção em HTTP 422 (ProblemDetails).
/// </summary>
public class DomainException(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
}

public static class Guarda
{
    public static string Texto(string? valor, string campo, int max)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new DomainException("CAMPO_OBRIGATORIO", $"{campo} é obrigatório.");
        var limpo = valor.Trim();
        if (limpo.Length > max)
            throw new DomainException("CAMPO_INVALIDO", $"{campo} deve ter no máximo {max} caracteres.");
        return limpo;
    }

    public static string? TextoOpcional(string? valor, string campo, int max)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        return Texto(valor, campo, max);
    }

    public static decimal Dinheiro(decimal valor, string campo, bool permiteZero = false)
    {
        if (valor < 0 || (!permiteZero && valor == 0))
            throw new DomainException("VALOR_INVALIDO", $"{campo} deve ser maior que zero.");
        return decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
    }
}
