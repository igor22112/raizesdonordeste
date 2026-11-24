using System.Text.Json;
using System.Text.Json.Serialization;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Domain.Auditoria;

namespace RaizesDoNordeste.Application.Comum;

public interface IAuditoria
{
    /// <summary>
    /// Adiciona o registro ao contexto. Ele é gravado no mesmo SaveChanges da operação auditada,
    /// garantindo que não existe operação sensível sem registro (e vice-versa).
    /// Não inclua dados pessoais em <paramref name="detalhes"/>.
    /// </summary>
    void Registrar(string acao, string entidade, object entidadeId, object? detalhes = null, Guid? unidadeId = null);
}

internal sealed class ServicoAuditoria(IAppDbContext db, IUsuarioAtual usuario, TimeProvider relogio) : IAuditoria
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public void Registrar(string acao, string entidade, object entidadeId, object? detalhes = null, Guid? unidadeId = null)
    {
        db.Auditoria.Add(new RegistroAuditoria(
            relogio.GetUtcNow(),
            usuario.Id,
            usuario.Perfil,
            unidadeId ?? usuario.UnidadeId,
            acao,
            entidade,
            entidadeId.ToString()!,
            detalhes is null ? null : JsonSerializer.Serialize(detalhes, Json),
            usuario.Ip,
            usuario.CorrelacaoId));
    }
}
