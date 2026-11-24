using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Domain.Identidade;

namespace RaizesDoNordeste.Infrastructure.Seguranca;

public sealed class JwtOpcoes
{
    public const string Secao = "Jwt";

    [Required] public string Emissor { get; set; } = "raizes-do-nordeste";
    [Required] public string Audiencia { get; set; } = "raizes-do-nordeste-api";

    /// <summary>Chave HMAC (mínimo 32 caracteres). Vem de variável de ambiente/cofre, nunca do repositório.</summary>
    [Required, MinLength(32)] public string Chave { get; set; } = "";
    [Range(5, 1440)] public int ExpiracaoMinutos { get; set; } = 60;

    public SymmetricSecurityKey ChaveAssinatura() => new(Encoding.UTF8.GetBytes(Chave));
}

public static class ClaimsApp
{
    public const string Perfil = "role";
    public const string UnidadeId = "unidade_id";
    public const string Nome = "name";
    public const string Sub = "sub";
}

internal sealed class GeradorTokenJwt(IOptions<JwtOpcoes> opcoes, TimeProvider relogio) : IGeradorToken
{
    private readonly JsonWebTokenHandler _handler = new();

    public TokenGerado Gerar(Usuario usuario)
    {
        var o = opcoes.Value;
        var agora = relogio.GetUtcNow();
        var expira = agora.AddMinutes(o.ExpiracaoMinutos);

        // Não colocamos e-mail ou outros dados pessoais no token (ele trafega e pode ser logado por proxies).
        var claims = new List<Claim>
        {
            new(ClaimsApp.Sub, usuario.Id.ToString()),
            new(ClaimsApp.Nome, usuario.Nome),
            new(ClaimsApp.Perfil, usuario.Perfil.ToString())
        };
        if (usuario.UnidadeId is { } unidade) claims.Add(new Claim(ClaimsApp.UnidadeId, unidade.ToString()));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Emissor,
            Audience = o.Audiencia,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expira.UtcDateTime,
            SigningCredentials = new SigningCredentials(o.ChaveAssinatura(), SecurityAlgorithms.HmacSha256)
        });
        return new TokenGerado(token, expira);
    }
}

/// <summary>PBKDF2 (HMAC-SHA512, 100 mil iterações, salt aleatório) via ASP.NET Core Identity.</summary>
internal sealed class SenhaHasher : ISenhaHasher
{
    private readonly PasswordHasher<Usuario> _hasher = new();

    public string Gerar(string senha) => _hasher.HashPassword(null!, senha);

    public bool Verificar(string hash, string senha) =>
        _hasher.VerifyHashedPassword(null!, hash, senha) != PasswordVerificationResult.Failed;
}
