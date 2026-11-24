using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Identidade;

namespace RaizesDoNordeste.Application.Identidade;

public sealed record RegistrarClienteRequest(
    string Nome,
    string Email,
    string Senha,
    DateOnly? DataNascimento,
    string? Telefone,
    bool AceiteTermos,
    bool ConsentimentoFidelidade,
    bool ConsentimentoCampanhasPersonalizadas);

public sealed record LoginRequest(string Email, string Senha);

public sealed record CriarUsuarioInternoRequest(string Nome, string Email, string Senha, Perfil Perfil, Guid? UnidadeId);

public sealed record UsuarioDto(Guid Id, string Nome, string Email, Perfil Perfil, Guid? UnidadeId);

public sealed record TokenResponse(string AccessToken, string TipoToken, DateTimeOffset ExpiraEm, UsuarioDto Usuario);

internal static class RegrasSenha
{
    public static IRuleBuilderOptions<T, string> SenhaForte<T>(this IRuleBuilder<T, string> regra) => regra
        .NotEmpty()
        .MinimumLength(8).WithMessage("A senha deve ter ao menos 8 caracteres.")
        .MaximumLength(128)
        .Matches("[A-Za-z]").WithMessage("A senha deve conter ao menos uma letra.")
        .Matches("[0-9]").WithMessage("A senha deve conter ao menos um número.");
}

public sealed class RegistrarClienteValidator : AbstractValidator<RegistrarClienteRequest>
{
    public RegistrarClienteValidator(TimeProvider relogio)
    {
        RuleFor(x => x.Nome).NotEmpty().MinimumLength(2).MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Senha).SenhaForte();
        RuleFor(x => x.Telefone).MaximumLength(20).Matches(@"^[0-9+()\-\s]*$").When(x => x.Telefone is not null);
        RuleFor(x => x.AceiteTermos).Equal(true).WithMessage("É necessário aceitar os termos de uso e a política de privacidade.");
        // Dados de crianças exigem consentimento dos pais (LGPD art. 14), fora do escopo: exigimos 12+ anos quando a data é informada.
        RuleFor(x => x.DataNascimento)
            .Must(d => d is null || d.Value.AddYears(12) <= DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime))
            .WithMessage("Cadastro permitido a partir de 12 anos.");
    }
}

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Senha).NotEmpty().MaximumLength(128);
    }
}

public sealed class CriarUsuarioInternoValidator : AbstractValidator<CriarUsuarioInternoRequest>
{
    public CriarUsuarioInternoValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MinimumLength(2).MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Senha).SenhaForte();
        RuleFor(x => x.Perfil).IsInEnum().NotEqual(Perfil.Cliente).WithMessage("Clientes se cadastram pelo endpoint público de registro.");
    }
}

public sealed class AutenticacaoService(
    IAppDbContext db,
    ISenhaHasher hasher,
    IGeradorToken tokens,
    IUsuarioAtual usuarioAtual,
    IAuditoria auditoria,
    TimeProvider relogio,
    IOptions<PrivacidadeOpcoes> privacidade,
    IValidator<RegistrarClienteRequest> validadorRegistro,
    IValidator<LoginRequest> validadorLogin,
    IValidator<CriarUsuarioInternoRequest> validadorInterno,
    ILogger<AutenticacaoService> logger)
{
    public async Task<UsuarioDto> RegistrarClienteAsync(RegistrarClienteRequest req, CancellationToken ct)
    {
        await validadorRegistro.ValidateAndThrowAsync(req, ct);
        var email = Usuario.NormalizarEmail(req.Email);
        await GarantirEmailLivreAsync(email, ct);

        var agora = relogio.GetUtcNow();
        var usuario = new Usuario(req.Nome, email, hasher.Gerar(req.Senha), Perfil.Cliente, null, agora);
        var cliente = new Cliente(usuario.Id, req.DataNascimento, req.Telefone);
        var versao = privacidade.Value.VersaoTermoAtual;

        db.Usuarios.Add(usuario);
        db.Clientes.Add(cliente);
        db.Consentimentos.AddRange(
            new Consentimento(cliente.Id, TipoConsentimento.TermosDeUso, true, versao, agora, "cadastro"),
            new Consentimento(cliente.Id, TipoConsentimento.ProgramaFidelidade, req.ConsentimentoFidelidade, versao, agora, "cadastro"),
            new Consentimento(cliente.Id, TipoConsentimento.CampanhasPersonalizadas, req.ConsentimentoCampanhasPersonalizadas, versao, agora, "cadastro"));

        await SalvarTratandoEmailDuplicadoAsync(ct);
        return ParaDto(usuario);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        await validadorLogin.ValidateAndThrowAsync(req, ct);
        var email = req.Email.Trim().ToLowerInvariant();
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Mesma mensagem para e-mail inexistente e senha errada: não revela quais e-mails existem.
        if (usuario is null || !usuario.Ativo || !hasher.Verificar(usuario.SenhaHash, req.Senha))
        {
            logger.LogWarning("Falha de login para {EmailMascarado}", MascararEmail(email));
            throw new NaoAutenticadoException();
        }

        var token = tokens.Gerar(usuario);
        return new TokenResponse(token.AccessToken, "Bearer", token.ExpiraEm, ParaDto(usuario));
    }

    public async Task<UsuarioDto> CriarUsuarioInternoAsync(CriarUsuarioInternoRequest req, CancellationToken ct)
    {
        usuarioAtual.GarantirPerfil(Perfil.AdminMatriz);
        await validadorInterno.ValidateAndThrowAsync(req, ct);
        var email = Usuario.NormalizarEmail(req.Email);
        await GarantirEmailLivreAsync(email, ct);

        if (req.UnidadeId is { } unidadeId && !await db.Unidades.AnyAsync(u => u.Id == unidadeId, ct))
            throw new NaoEncontradoException("Unidade");

        var usuario = new Usuario(req.Nome, email, hasher.Gerar(req.Senha), req.Perfil, req.UnidadeId, relogio.GetUtcNow());
        db.Usuarios.Add(usuario);
        auditoria.Registrar(AcoesAuditoria.UsuarioCriado, nameof(Usuario), usuario.Id, new { usuario.Perfil, usuario.UnidadeId }, req.UnidadeId);
        await SalvarTratandoEmailDuplicadoAsync(ct);
        return ParaDto(usuario);
    }

    public async Task<UsuarioDto> ObterAtualAsync(CancellationToken ct)
    {
        var id = usuarioAtual.IdObrigatorio();
        var usuario = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NaoEncontradoException("Usuário");
        return ParaDto(usuario);
    }

    private async Task GarantirEmailLivreAsync(string email, CancellationToken ct)
    {
        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct))
            throw new ConflitoException("EMAIL_EM_USO", "Já existe uma conta com este e-mail.");
    }

    private async Task SalvarTratandoEmailDuplicadoAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Usuarios_Email") == true)
        {
            // Duas requisições simultâneas com o mesmo e-mail: o índice único é a garantia final.
            throw new ConflitoException("EMAIL_EM_USO", "Já existe uma conta com este e-mail.");
        }
    }

    internal static string MascararEmail(string email)
    {
        var arroba = email.IndexOf('@');
        if (arroba <= 1) return "***";
        return $"{email[0]}***{email[arroba..]}";
    }

    private static UsuarioDto ParaDto(Usuario u) => new(u.Id, u.Nome, u.Email, u.Perfil, u.UnidadeId);
}
