using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Unidades;

namespace RaizesDoNordeste.Application.Unidades;

public sealed record UnidadeRequest(
    string Nome,
    string Cidade,
    string Uf,
    Regiao Regiao,
    TipoUnidade Tipo,
    string FusoHorario,
    TimeOnly HoraAbertura,
    TimeOnly HoraFechamento,
    bool Ativa = true);

public sealed record UnidadeDto(
    Guid Id, string Nome, string Cidade, string Uf, Regiao Regiao, TipoUnidade Tipo, string FusoHorario,
    TimeOnly HoraAbertura, TimeOnly HoraFechamento, bool Ativa, bool AbertaAgora);

public sealed class UnidadeRequestValidator : AbstractValidator<UnidadeRequest>
{
    public UnidadeRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Cidade).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Uf).NotEmpty().Length(2);
        RuleFor(x => x.Regiao).IsInEnum();
        RuleFor(x => x.Tipo).IsInEnum();
        RuleFor(x => x.FusoHorario).NotEmpty().MaximumLength(60)
            .Must(f => TimeZoneInfo.TryFindSystemTimeZoneById(f, out _)).WithMessage("Fuso horário IANA inválido (ex.: America/Recife).");
    }
}

public sealed class UnidadeService(IAppDbContext db, IUsuarioAtual usuario, TimeProvider relogio, IValidator<UnidadeRequest> validador)
{
    public async Task<IReadOnlyList<UnidadeDto>> ListarAsync(Regiao? regiao, CancellationToken ct)
    {
        var consulta = db.Unidades.AsNoTracking();
        if (!usuario.EhAdmin()) consulta = consulta.Where(u => u.Ativa);
        if (regiao is not null) consulta = consulta.Where(u => u.Regiao == regiao);
        var unidades = await consulta.OrderBy(u => u.Uf).ThenBy(u => u.Nome).ToListAsync(ct);
        var agora = relogio.GetUtcNow();
        return unidades.Select(u => ParaDto(u, agora)).ToList();
    }

    public async Task<UnidadeDto> ObterAsync(Guid id, CancellationToken ct)
    {
        var unidade = await db.Unidades.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unidade is null || (!unidade.Ativa && !usuario.EhAdmin())) throw new NaoEncontradoException("Unidade");
        return ParaDto(unidade, relogio.GetUtcNow());
    }

    public async Task<UnidadeDto> CriarAsync(UnidadeRequest req, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        await validador.ValidateAndThrowAsync(req, ct);
        var unidade = new Unidade(req.Nome, req.Cidade, req.Uf, req.Regiao, req.Tipo, req.FusoHorario, req.HoraAbertura, req.HoraFechamento);
        if (!req.Ativa)
            unidade.Atualizar(req.Nome, req.Cidade, req.Uf, req.Regiao, req.Tipo, req.FusoHorario, req.HoraAbertura, req.HoraFechamento, false);
        db.Unidades.Add(unidade);
        await db.SaveChangesAsync(ct);
        return ParaDto(unidade, relogio.GetUtcNow());
    }

    public async Task<UnidadeDto> AtualizarAsync(Guid id, UnidadeRequest req, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        await validador.ValidateAndThrowAsync(req, ct);
        var unidade = await db.Unidades.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NaoEncontradoException("Unidade");
        unidade.Atualizar(req.Nome, req.Cidade, req.Uf, req.Regiao, req.Tipo, req.FusoHorario, req.HoraAbertura, req.HoraFechamento, req.Ativa);
        await db.SaveChangesAsync(ct);
        return ParaDto(unidade, relogio.GetUtcNow());
    }

    internal static UnidadeDto ParaDto(Unidade u, DateTimeOffset agora) =>
        new(u.Id, u.Nome, u.Cidade, u.Uf, u.Regiao, u.Tipo, u.FusoHorario, u.HoraAbertura, u.HoraFechamento, u.Ativa, u.EstaAberta(agora));
}
