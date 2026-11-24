using RaizesDoNordeste.Domain.Comum;

namespace RaizesDoNordeste.Domain.Identidade;

/// <summary>
/// Perfis de acesso. Atendente, Cozinha, Gerente e Totem são vinculados a uma unidade.
/// Totem é uma conta de dispositivo (auto-atendimento) da unidade.
/// </summary>
public enum Perfil { Cliente, Atendente, Cozinha, Gerente, Totem, AdminMatriz }

public sealed class Usuario : Entidade
{
    private Usuario() { }

    public Usuario(string nome, string email, string senhaHash, Perfil perfil, Guid? unidadeId, DateTimeOffset criadoEm)
    {
        Nome = Guarda.Texto(nome, "Nome", 120);
        Email = NormalizarEmail(email);
        SenhaHash = senhaHash;
        Perfil = perfil;
        if (perfil is Perfil.Atendente or Perfil.Cozinha or Perfil.Gerente or Perfil.Totem)
        {
            if (unidadeId is null)
                throw new DomainException("UNIDADE_OBRIGATORIA", $"O perfil {perfil} exige vínculo com uma unidade.");
        }
        else if (unidadeId is not null)
        {
            throw new DomainException("UNIDADE_INDEVIDA", $"O perfil {perfil} não é vinculado a unidade.");
        }
        UnidadeId = unidadeId;
        Ativo = true;
        CriadoEm = criadoEm;
    }

    public string Nome { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string SenhaHash { get; private set; } = null!;
    public Perfil Perfil { get; private set; }
    public Guid? UnidadeId { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public static string NormalizarEmail(string email) => Guarda.Texto(email, "E-mail", 200).ToLowerInvariant();

    /// <summary>LGPD: remove dados que identificam a pessoa e bloqueia o acesso.</summary>
    public void Anonimizar(string senhaHashAleatoria)
    {
        Nome = "Cliente anonimizado";
        Email = $"anonimizado-{Id:N}@anonimizado.invalid";
        SenhaHash = senhaHashAleatoria;
        Ativo = false;
    }

    public void AtualizarSenhaHash(string novoHash) => SenhaHash = novoHash;
}
