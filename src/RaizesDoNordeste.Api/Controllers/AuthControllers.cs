using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RaizesDoNordeste.Api.Infra;
using RaizesDoNordeste.Application.Identidade;

namespace RaizesDoNordeste.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
[Tags("Autenticação")]
public sealed class AuthController(AutenticacaoService servico) : ControllerBase
{
    /// <summary>Cadastro de cliente (app). Exige aceite dos termos; consentimentos opcionais são registrados.</summary>
    [HttpPost("registro")]
    [AllowAnonymous]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioDto>> Registrar(RegistrarClienteRequest req, CancellationToken ct)
    {
        var usuario = await servico.RegistrarClienteAsync(req, ct);
        return Created("/api/v1/auth/me", usuario);
    }

    /// <summary>Autentica e devolve um JWT. Limitado por IP para dificultar força bruta.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<TokenResponse> Login(LoginRequest req, CancellationToken ct) => servico.LoginAsync(req, ct);

    [HttpGet("me")]
    [Authorize]
    public Task<UsuarioDto> Me(CancellationToken ct) => servico.ObterAtualAsync(ct);
}

[ApiController]
[Route("api/v1/usuarios")]
[Authorize(Roles = Perfis.Admin)]
[Tags("Autenticação")]
public sealed class UsuariosController(AutenticacaoService servico) : ControllerBase
{
    /// <summary>Cria conta interna (gerente, atendente, cozinha, totem ou outro administrador).</summary>
    [HttpPost]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UsuarioDto>> Criar(CriarUsuarioInternoRequest req, CancellationToken ct)
    {
        var usuario = await servico.CriarUsuarioInternoAsync(req, ct);
        return StatusCode(StatusCodes.Status201Created, usuario);
    }
}
