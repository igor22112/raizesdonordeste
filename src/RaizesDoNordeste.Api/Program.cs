using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RaizesDoNordeste.Api.Infra;
using RaizesDoNordeste.Application;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Infrastructure;
using RaizesDoNordeste.Infrastructure.Persistencia;
using RaizesDoNordeste.Infrastructure.Seed;
using RaizesDoNordeste.Infrastructure.Seguranca;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUsuarioAtual, UsuarioAtualHttp>();

builder.Services.AddControllers(o =>
    {
        // Documenta no OpenAPI o padrão de erro (ProblemDetails) para todos os endpoints.
        foreach (var status in new[] { 400, 401, 403, 404, 409, 429, 500 })
            o.Filters.Add(new ProducesResponseTypeAttribute(typeof(ProblemDetails), status));
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        // JSON malformado, tipo inválido ou enum desconhecido: mesmo formato de erro das demais falhas.
        var fabrica = ctx.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problema = fabrica.CreateProblemDetails(ctx.HttpContext, 400, "Dados inválidos", "https://httpstatuses.io/400",
            "Um ou mais campos são inválidos.", ctx.HttpContext.Request.Path);
        problema.Extensions["codigo"] = "VALIDACAO";
        problema.Extensions["erros"] = ctx.ModelState.Where(e => e.Value?.Errors.Count > 0).ToDictionary(
            e => string.IsNullOrEmpty(e.Key) ? "corpo" : e.Key.TrimStart('$', '.'),
            e => e.Value!.Errors.Select(x => string.IsNullOrEmpty(x.ErrorMessage) ? "Valor inválido." : x.ErrorMessage).ToArray());
        return new ObjectResult(problema) { StatusCode = 400, ContentTypes = { "application/problem+json" } };
    });
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    // Todas as respostas de erro (exceções, 401/403 da autenticação, 404 de rota, 429) saem no mesmo formato:
    // type, title, status, detail, instance (rota), codigo, timestamp, traceId, correlacaoId.
    var pd = ctx.ProblemDetails;
    pd.Instance ??= ctx.HttpContext.Request.Path;
    pd.Extensions.TryAdd("codigo", pd.Status switch
    {
        400 => "VALIDACAO",
        401 => "NAO_AUTENTICADO",
        403 => "ACESSO_NEGADO",
        404 => "NAO_ENCONTRADO",
        405 => "METODO_NAO_PERMITIDO",
        409 => "CONFLITO",
        415 => "TIPO_DE_CONTEUDO_INVALIDO",
        429 => "LIMITE_DE_REQUISICOES",
        _ => "ERRO_INTERNO"
    });
    if (pd.Status == 401) pd.Detail ??= "Autenticação necessária: envie um token JWT válido no header Authorization.";
    if (pd.Status == 403) pd.Detail ??= "Seu perfil não tem permissão para este recurso.";
    pd.Extensions["timestamp"] = DateTimeOffset.UtcNow;
    pd.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    if (ctx.HttpContext.Items[CorrelacaoMiddleware.Chave] is string correlacao)
        pd.Extensions["correlacaoId"] = correlacao;
});
builder.Services.AddExceptionHandler<ManipuladorExcecoes>();

// ---------- Autenticação e autorização (JWT stateless: qualquer instância valida o token) ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOpcoes>>((o, jwtOpcoes) =>
    {
        var jwt = jwtOpcoes.Value;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Emissor,
            ValidAudience = jwt.Audiencia,
            IssuerSigningKey = jwt.ChaveAssinatura(),
            ValidateIssuerSigningKey = true,
            NameClaimType = ClaimsApp.Nome,
            RoleClaimType = ClaimsApp.Perfil,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// ---------- Rate limiting: protege a API em picos e contra abuso (força bruta no login) ----------
var limites = config.GetSection("RateLimit");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var chave = ctx.User.FindFirstValue(ClaimsApp.Sub) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo";
        return RateLimitPartition.GetTokenBucketLimiter(chave, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = limites.GetValue("RequisicoesPorJanela", 200),
            TokensPerPeriod = limites.GetValue("RequisicoesPorJanela", 200),
            ReplenishmentPeriod = TimeSpan.FromSeconds(limites.GetValue("JanelaSegundos", 10)),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limites.GetValue("LoginPorMinuto", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = 429, Title = "Muitas requisições", Type = "https://httpstatuses.io/429",
                Detail = "Limite de requisições excedido. Tente novamente em instantes."
            }
        });
    };
});

// ---------- Cache do cardápio: é a rota mais lida no pico; alguns segundos de cache aliviam o banco ----------
var segundosCacheCardapio = config.GetValue("Cache:CardapioSegundos", 15);
builder.Services.AddOutputCache(o => o.AddPolicy("Cardapio", p =>
{
    if (segundosCacheCardapio <= 0) p.NoCache();
    else p.Expire(TimeSpan.FromSeconds(segundosCacheCardapio)).SetVaryByRouteValue("unidadeId");
}));

builder.Services.AddCors(o => o.AddPolicy("Frontend", p => p
    .WithOrigins(config.GetSection("Cors:Origens").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(CorrelacaoMiddleware.Header, "Location")));

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("sqlserver", tags: ["ready"]);
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<DocumentacaoOpenApi>());
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelacaoMiddleware>();
// O log de requisição fica por fora do tratamento de exceções para registrar o status final (ex.: 422, não 500).
app.UseSerilogRequestLogging(o => o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} respondeu {StatusCode} em {Elapsed:0.0} ms");
app.UseExceptionHandler();
app.UseStatusCodePages();

if (config.GetValue("Documentacao:Habilitada", app.Environment.IsDevelopment()))
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("Raízes do Nordeste — API"));
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseOutputCache();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

await InicializarBancoAsync(app);
await app.RunAsync();

static async Task InicializarBancoAsync(WebApplication app)
{
    // Em containers o SQL Server pode demorar a aceitar conexões: tentamos por até ~60s.
    for (var tentativa = 1; ; tentativa++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DataSeeder>().ExecutarAsync();
            return;
        }
        catch (Exception ex) when (tentativa < 12)
        {
            app.Logger.LogWarning(ex, "Banco indisponível (tentativa {Tentativa}); nova tentativa em 5s.", tentativa);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

public partial class Program;
