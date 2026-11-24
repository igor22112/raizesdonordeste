using RaizesDoNordeste.Domain.Comum;

namespace RaizesDoNordeste.Domain.Pagamentos;

public enum MetodoPagamento { Pix, CartaoCredito, CartaoDebito }

public enum StatusPagamento
{
    /// <summary>Solicitado ao gateway; aguardando confirmação assíncrona (webhook).</summary>
    Pendente,
    Aprovado,
    Recusado,

    /// <summary>Não foi possível falar com o gateway (indisponível/timeout). Pode tentar de novo.</summary>
    Falhou,
    Estornado,

    /// <summary>Estorno solicitado, mas o gateway não respondeu. A falha fica registrada e auditada.</summary>
    EstornoPendente
}

/// <summary>
/// Registro de uma tentativa de pagamento. O sistema NÃO processa o pagamento (§5 do estudo
/// de caso): apenas solicita ao gateway externo, recebe o resultado e registra.
/// O Id deste registro é usado como chave de idempotência no gateway.
/// </summary>
public sealed class Pagamento : Entidade
{
    private Pagamento() { }

    public Pagamento(Guid pedidoId, decimal valor, MetodoPagamento metodo, DateTimeOffset agora)
    {
        PedidoId = pedidoId;
        Valor = Guarda.Dinheiro(valor, "Valor");
        Metodo = metodo;
        Status = StatusPagamento.Pendente;
        CriadoEm = agora;
        AtualizadoEm = agora;
    }

    public Guid PedidoId { get; private set; }
    public decimal Valor { get; private set; }
    public MetodoPagamento Metodo { get; private set; }
    public StatusPagamento Status { get; private set; }
    public string? TransacaoExternaId { get; private set; }
    public string? Detalhe { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }
    public byte[] Versao { get; private set; } = [];

    public bool EstaFinalizado => Status is StatusPagamento.Aprovado or StatusPagamento.Recusado
        or StatusPagamento.Falhou or StatusPagamento.Estornado;

    public void RegistrarEnvio(string transacaoExternaId, DateTimeOffset agora)
    {
        GarantirStatus(StatusPagamento.Pendente);
        TransacaoExternaId = Guarda.Texto(transacaoExternaId, "Transação externa", 100);
        AtualizadoEm = agora;
    }

    public void Aprovar(string? transacaoExternaId, DateTimeOffset agora)
    {
        GarantirStatus(StatusPagamento.Pendente);
        TransacaoExternaId ??= transacaoExternaId;
        Status = StatusPagamento.Aprovado;
        AtualizadoEm = agora;
    }

    public void Recusar(string? motivo, DateTimeOffset agora)
    {
        GarantirStatus(StatusPagamento.Pendente);
        Status = StatusPagamento.Recusado;
        Detalhe = Truncar(motivo);
        AtualizadoEm = agora;
    }

    public void MarcarFalha(string motivo, DateTimeOffset agora)
    {
        GarantirStatus(StatusPagamento.Pendente);
        Status = StatusPagamento.Falhou;
        Detalhe = Truncar(motivo);
        AtualizadoEm = agora;
    }

    public void Estornar(DateTimeOffset agora)
    {
        if (Status != StatusPagamento.Aprovado)
            throw new DomainException("ESTORNO_INVALIDO", $"Não é possível estornar um pagamento {Status}.");
        Status = StatusPagamento.Estornado;
        AtualizadoEm = agora;
    }

    public void MarcarEstornoPendente(string motivo, DateTimeOffset agora)
    {
        if (Status != StatusPagamento.Aprovado)
            throw new DomainException("ESTORNO_INVALIDO", $"Não é possível estornar um pagamento {Status}.");
        Status = StatusPagamento.EstornoPendente;
        Detalhe = Truncar(motivo);
        AtualizadoEm = agora;
    }

    private void GarantirStatus(StatusPagamento esperado)
    {
        if (Status != esperado)
            throw new DomainException("PAGAMENTO_STATUS_INVALIDO", $"Operação inválida para pagamento {Status}.");
    }

    private static string? Truncar(string? texto) => texto is null ? null : texto.Length <= 300 ? texto : texto[..300];
}
