using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Identidade;

namespace RaizesDoNordeste.Domain.Auditoria;

/// <summary>Ações sensíveis que a matriz precisa rastrear (§3 e §4 do estudo de caso).</summary>
public static class AcoesAuditoria
{
    public const string PedidoCriado = "PedidoCriado";
    public const string PedidoStatusAlterado = "PedidoStatusAlterado";
    public const string PedidoCancelado = "PedidoCancelado";
    public const string DescontoAplicado = "DescontoAplicado";
    public const string EstoqueAjustado = "EstoqueAjustado";
    public const string PrecoLocalAlterado = "PrecoLocalAlterado";
    public const string PagamentoEstornado = "PagamentoEstornado";
    public const string DadosPessoaisExportados = "DadosPessoaisExportados";
    public const string DadosPessoaisAcessados = "DadosPessoaisAcessados";
    public const string ClienteAnonimizado = "ClienteAnonimizado";
    public const string ConsentimentoAlterado = "ConsentimentoAlterado";
    public const string UsuarioCriado = "UsuarioCriado";
}

/// <summary>
/// Registro imutável (append-only). O DbContext bloqueia UPDATE/DELETE desta entidade
/// e a API não expõe operações de escrita sobre ela.
/// </summary>
public sealed class RegistroAuditoria : Entidade
{
    private RegistroAuditoria() { }

    public RegistroAuditoria(DateTimeOffset ocorridoEm, Guid? usuarioId, Perfil? perfil, Guid? unidadeId, string acao,
        string entidade, string entidadeId, string? detalhesJson, string? ip, string? correlacaoId)
    {
        OcorridoEm = ocorridoEm;
        UsuarioId = usuarioId;
        Perfil = perfil;
        UnidadeId = unidadeId;
        Acao = acao;
        Entidade = entidade;
        EntidadeId = entidadeId;
        DetalhesJson = detalhesJson;
        Ip = ip;
        CorrelacaoId = correlacaoId;
    }

    public DateTimeOffset OcorridoEm { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public Perfil? Perfil { get; private set; }
    public Guid? UnidadeId { get; private set; }
    public string Acao { get; private set; } = null!;
    public string Entidade { get; private set; } = null!;
    public string EntidadeId { get; private set; } = null!;
    public string? DetalhesJson { get; private set; }
    public string? Ip { get; private set; }
    public string? CorrelacaoId { get; private set; }
}
