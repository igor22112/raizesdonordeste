using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Domain.Identidade;

namespace RaizesDoNordeste.Application.Comum;

public sealed class NaoEncontradoException(string recurso) : Exception($"{recurso} não encontrado(a).");

public sealed class AcessoNegadoException(string mensagem = "Você não tem permissão para esta operação.") : Exception(mensagem);

public sealed class NaoAutenticadoException(string mensagem = "Credenciais inválidas.") : Exception(mensagem);

/// <summary>Conflito de estado (HTTP 409), ex.: e-mail já cadastrado.</summary>
public sealed class ConflitoException(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
}

/// <summary>Dependência externa indisponível (HTTP 503).</summary>
public sealed class ServicoIndisponivelException(string mensagem, int retryAfterSegundos = 30) : Exception(mensagem)
{
    public int RetryAfterSegundos { get; } = retryAfterSegundos;
}

public sealed record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Pagina, int Tamanho, int Total);

public static class UsuarioAtualExtensoes
{
    public static Guid IdObrigatorio(this IUsuarioAtual u) =>
        u.Id ?? throw new NaoAutenticadoException("Autenticação necessária.");

    public static bool EhAdmin(this IUsuarioAtual u) => u.Perfil == Perfil.AdminMatriz;

    public static bool EhEquipeDaUnidade(this IUsuarioAtual u, Guid unidadeId) =>
        u.Perfil is Perfil.Atendente or Perfil.Cozinha or Perfil.Gerente && u.UnidadeId == unidadeId;

    /// <summary>Admin acessa qualquer unidade; demais perfis só a própria.</summary>
    public static void GarantirAcessoUnidade(this IUsuarioAtual u, Guid unidadeId, params Perfil[] perfisPermitidos)
    {
        if (u.EhAdmin()) return;
        if (u.UnidadeId != unidadeId || (perfisPermitidos.Length > 0 && !perfisPermitidos.Contains(u.Perfil!.Value)))
            throw new AcessoNegadoException("Acesso restrito à equipe desta unidade.");
    }

    public static void GarantirPerfil(this IUsuarioAtual u, params Perfil[] perfis)
    {
        if (u.Perfil is not { } p || !perfis.Contains(p)) throw new AcessoNegadoException();
    }
}

public static class Paginacao
{
    public const int TamanhoMaximo = 100;

    public static (int pagina, int tamanho) Normalizar(int? pagina, int? tamanho) =>
        (Math.Max(1, pagina ?? 1), Math.Clamp(tamanho ?? 20, 1, TamanhoMaximo));
}

public sealed class PedidoOpcoes
{
    public const string Secao = "Pedidos";

    /// <summary>Tempo para pagar antes de o pedido expirar e liberar o estoque.</summary>
    public int PrazoPagamentoMinutos { get; set; } = 15;

    /// <summary>
    /// Pedidos com pagamento pendente mais recente que isto não expiram (o webhook pode estar a caminho).
    /// </summary>
    public int ToleranciaPagamentoPendenteMinutos { get; set; } = 10;
}

public sealed class PrivacidadeOpcoes
{
    public const string Secao = "Privacidade";
    public string VersaoTermoAtual { get; set; } = "1.0";
}
