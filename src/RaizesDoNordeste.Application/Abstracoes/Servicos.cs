using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;

namespace RaizesDoNordeste.Application.Abstracoes;

/// <summary>Usuário autenticado na requisição atual (preenchido a partir do JWT).</summary>
public interface IUsuarioAtual
{
    Guid? Id { get; }
    Perfil? Perfil { get; }
    Guid? UnidadeId { get; }
    string? Ip { get; }
    string? CorrelacaoId { get; }
}

public interface ISenhaHasher
{
    string Gerar(string senha);
    bool Verificar(string hash, string senha);
}

public sealed record TokenGerado(string AccessToken, DateTimeOffset ExpiraEm);

public interface IGeradorToken
{
    TokenGerado Gerar(Usuario usuario);
}

public sealed record SolicitacaoPagamento(Guid PagamentoId, Guid PedidoId, decimal Valor, MetodoPagamento Metodo);

public sealed record RespostaSolicitacaoPagamento(bool Aceita, string? TransacaoId, string? MotivoRecusa);

/// <summary>
/// Porta para o serviço externo de pagamentos. A implementação (Infrastructure) aplica
/// timeout, re-tentativa com backoff e circuit breaker. Re-tentar é seguro porque toda
/// chamada envia uma chave de idempotência.
/// </summary>
public interface IGatewayPagamento
{
    /// <exception cref="GatewayIndisponivelException">Gateway fora do ar, lento ou com circuito aberto.</exception>
    Task<RespostaSolicitacaoPagamento> SolicitarAsync(SolicitacaoPagamento solicitacao, CancellationToken ct);

    /// <exception cref="GatewayIndisponivelException">Gateway fora do ar, lento ou com circuito aberto.</exception>
    Task EstornarAsync(string transacaoId, Guid chaveIdempotencia, CancellationToken ct);
}

public sealed class GatewayIndisponivelException(string mensagem, Exception? inner = null) : Exception(mensagem, inner);

/// <summary>Publica eventos para consumidores externos (painel da cozinha, notificações, BI).</summary>
public interface IPublicadorEventos
{
    Task PublicarAsync(string tipo, string payloadJson, CancellationToken ct);
}
