using RaizesDoNordeste.Domain.Comum;

namespace RaizesDoNordeste.Domain.Clientes;

/// <summary>
/// Dados do cliente final. Coletamos o mínimo necessário (LGPD, princípio da necessidade):
/// não há CPF nem endereço. Data de nascimento e telefone são opcionais.
/// O Id é o mesmo do Usuario (relação 1:1).
/// </summary>
public sealed class Cliente
{
    private Cliente() { }

    public Cliente(Guid usuarioId, DateOnly? dataNascimento, string? telefone)
    {
        Id = usuarioId;
        DataNascimento = dataNascimento;
        Telefone = Guarda.TextoOpcional(telefone, "Telefone", 20);
    }

    public Guid Id { get; private set; }
    public DateOnly? DataNascimento { get; private set; }
    public string? Telefone { get; private set; }

    /// <summary>Saldo de pontos (cache do extrato em MovimentacaoPontos). Alterado com UPDATE atômico.</summary>
    public int SaldoPontos { get; private set; }
    public bool Anonimizado { get; private set; }
    public DateTimeOffset? AnonimizadoEm { get; private set; }

    public void Anonimizar(DateTimeOffset agora)
    {
        DataNascimento = null;
        Telefone = null;
        Anonimizado = true;
        AnonimizadoEm = agora;
    }
}

/// <summary>
/// Finalidades de tratamento que exigem consentimento explícito.
/// TermosDeUso é obrigatório no cadastro; os demais são opcionais e revogáveis a qualquer momento.
/// </summary>
public enum TipoConsentimento
{
    TermosDeUso,
    ProgramaFidelidade,
    CampanhasPersonalizadas
}

/// <summary>
/// Registro histórico (append-only) de consentimento. O estado atual de cada tipo é o
/// registro mais recente. Guardar o histórico permite provar quando e em qual versão
/// do termo o titular consentiu (LGPD art. 8º, §2º).
/// </summary>
public sealed class Consentimento : Entidade
{
    private Consentimento() { }

    public Consentimento(Guid clienteId, TipoConsentimento tipo, bool concedido, string versaoTermo, DateTimeOffset registradoEm, string? origem)
    {
        ClienteId = clienteId;
        Tipo = tipo;
        Concedido = concedido;
        VersaoTermo = Guarda.Texto(versaoTermo, "Versão do termo", 20);
        RegistradoEm = registradoEm;
        Origem = Guarda.TextoOpcional(origem, "Origem", 60);
    }

    public Guid ClienteId { get; private set; }
    public TipoConsentimento Tipo { get; private set; }
    public bool Concedido { get; private set; }
    public string VersaoTermo { get; private set; } = null!;
    public DateTimeOffset RegistradoEm { get; private set; }
    public string? Origem { get; private set; }
}

public enum TipoMovimentacaoPontos { Credito, Debito, Estorno }

public sealed class MovimentacaoPontos : Entidade
{
    private MovimentacaoPontos() { }

    public MovimentacaoPontos(Guid clienteId, TipoMovimentacaoPontos tipo, int pontos, Guid? pedidoId, DateTimeOffset ocorridoEm)
    {
        if (pontos <= 0) throw new DomainException("PONTOS_INVALIDOS", "A movimentação deve ter pontos positivos.");
        ClienteId = clienteId;
        Tipo = tipo;
        Pontos = pontos;
        PedidoId = pedidoId;
        OcorridoEm = ocorridoEm;
    }

    public Guid ClienteId { get; private set; }
    public TipoMovimentacaoPontos Tipo { get; private set; }
    public int Pontos { get; private set; }
    public Guid? PedidoId { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Sinal da movimentação no saldo.</summary>
    public int Efeito => Tipo is TipoMovimentacaoPontos.Credito or TipoMovimentacaoPontos.Estorno ? Pontos : -Pontos;
}
