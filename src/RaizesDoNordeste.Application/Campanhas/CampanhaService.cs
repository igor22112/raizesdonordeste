using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Fidelidade;
using RaizesDoNordeste.Domain.Campanhas;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Unidades;

namespace RaizesDoNordeste.Application.Campanhas;

public sealed record CriarCampanhaRequest(
    string Nome,
    string? Descricao,
    decimal PercentualDesconto,
    DateTimeOffset InicioEm,
    DateTimeOffset FimEm,
    int? FrequenciaMinima,
    NivelFidelidade? NivelMinimo,
    Regiao? Regiao);

public sealed record CampanhaDto(
    Guid Id, string Nome, string? Descricao, decimal PercentualDesconto, DateTimeOffset InicioEm, DateTimeOffset FimEm,
    bool Ativa, bool Vigente, Segmentacao Segmentacao);

public sealed class CriarCampanhaValidator : AbstractValidator<CriarCampanhaRequest>
{
    public CriarCampanhaValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Descricao).MaximumLength(500);
        RuleFor(x => x.PercentualDesconto).InclusiveBetween(1, 30).WithMessage("Informe o desconto em % (1 a 30).");
        RuleFor(x => x.FimEm).GreaterThan(x => x.InicioEm);
        RuleFor(x => x.FrequenciaMinima).GreaterThan(0).When(x => x.FrequenciaMinima is not null);
        RuleFor(x => x.NivelMinimo).IsInEnum().When(x => x.NivelMinimo is not null);
        RuleFor(x => x.Regiao).IsInEnum().When(x => x.Regiao is not null);
    }
}

public sealed class CampanhaService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    ConsultasCliente consultas,
    TimeProvider relogio,
    IValidator<CriarCampanhaRequest> validador)
{
    public async Task<CampanhaDto> CriarAsync(CriarCampanhaRequest req, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        await validador.ValidateAndThrowAsync(req, ct);
        var campanha = new Campanha(req.Nome, req.Descricao, req.PercentualDesconto / 100m, req.InicioEm, req.FimEm,
            new Segmentacao(req.FrequenciaMinima, req.NivelMinimo, req.Regiao));
        db.Campanhas.Add(campanha);
        await db.SaveChangesAsync(ct);
        return ParaDto(campanha, relogio.GetUtcNow());
    }

    public async Task<IReadOnlyList<CampanhaDto>> ListarAsync(bool apenasVigentes, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        var agora = relogio.GetUtcNow();
        var consulta = db.Campanhas.AsNoTracking();
        if (apenasVigentes) consulta = consulta.Where(c => c.Ativa && c.InicioEm <= agora && c.FimEm >= agora);
        var lista = await consulta.OrderByDescending(c => c.InicioEm).ToListAsync(ct);
        return lista.Select(c => ParaDto(c, agora)).ToList();
    }

    public async Task DesativarAsync(Guid id, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        var campanha = await db.Campanhas.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NaoEncontradoException("Campanha");
        campanha.Desativar();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Campanhas vigentes para as quais o cliente logado é elegível.</summary>
    public async Task<IReadOnlyList<CampanhaDto>> ListarElegiveisAsync(Guid? unidadeId, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.Cliente);
        var clienteId = usuario.IdObrigatorio();
        var agora = relogio.GetUtcNow();

        Regiao? regiao = null;
        if (unidadeId is { } uid)
            regiao = await db.Unidades.Where(u => u.Id == uid).Select(u => (Regiao?)u.Regiao).FirstOrDefaultAsync(ct)
                ?? throw new NaoEncontradoException("Unidade");

        var perfil = await MontarPerfilAsync(clienteId, agora, ct);
        var vigentes = await db.Campanhas.AsNoTracking()
            .Where(c => c.Ativa && c.InicioEm <= agora && c.FimEm >= agora)
            .ToListAsync(ct);

        return vigentes
            .Where(c => ElegibilidadeCampanha.Avaliar(c, perfil, regiao, agora).Elegivel)
            .Select(c => ParaDto(c, agora))
            .ToList();
    }

    /// <summary>Usado na criação do pedido: valida a campanha escolhida e devolve o percentual.</summary>
    public async Task<decimal> PercentualParaPedidoAsync(Guid campanhaId, Guid clienteId, Regiao regiaoUnidade, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var campanha = await db.Campanhas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == campanhaId, ct)
            ?? throw new NaoEncontradoException("Campanha");
        var perfil = await MontarPerfilAsync(clienteId, agora, ct);
        var resultado = ElegibilidadeCampanha.Avaliar(campanha, perfil, regiaoUnidade, agora);
        if (!resultado.Elegivel) throw new DomainException("CAMPANHA_INELEGIVEL", resultado.Motivo!);
        return campanha.PercentualDesconto;
    }

    private async Task<PerfilCliente> MontarPerfilAsync(Guid clienteId, DateTimeOffset agora, CancellationToken ct)
    {
        if (!await db.Clientes.AnyAsync(c => c.Id == clienteId, ct))
            throw new NaoEncontradoException("Cliente");
        var pedidos = await consultas.PedidosEntreguesNaJanelaAsync(clienteId, agora, ct);
        var consentiu = await consultas.ConsentimentoAtivoAsync(clienteId, TipoConsentimento.CampanhasPersonalizadas, ct);
        return new PerfilCliente(pedidos, RegrasFidelidade.CalcularNivel(pedidos), consentiu);
    }

    private static CampanhaDto ParaDto(Campanha c, DateTimeOffset agora) =>
        new(c.Id, c.Nome, c.Descricao, c.PercentualDesconto * 100m, c.InicioEm, c.FimEm, c.Ativa, c.EstaVigente(agora), c.Segmentacao);
}
