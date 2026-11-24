using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.Application.Relatorios;

public enum AgrupamentoVendas { Unidade, Regiao, Canal }

public sealed record FiltroRelatorio(DateOnly De, DateOnly Ate, Guid? UnidadeId);

public sealed record LinhaVendasDto(string Chave, string Descricao, int Pedidos, decimal Faturamento, decimal TicketMedio, decimal Descontos);

public sealed record RelatorioVendasDto(DateOnly De, DateOnly Ate, AgrupamentoVendas Agrupamento, IReadOnlyList<LinhaVendasDto> Linhas,
    int TotalPedidos, decimal FaturamentoTotal, decimal TicketMedioGeral);

public sealed record ProdutoMaisVendidoDto(Guid ProdutoId, string Produto, int Quantidade, decimal Faturamento);

public sealed record ResumoAuditoriaDto(string Acao, int Ocorrencias);

public sealed record RegistroAuditoriaDto(Guid Id, DateTimeOffset OcorridoEm, Guid? UsuarioId, Perfil? Perfil, Guid? UnidadeId, string Acao,
    string Entidade, string EntidadeId, string? Detalhes, string? CorrelacaoId);

public sealed record FiltroAuditoria(string? Acao, string? Entidade, string? EntidadeId, Guid? UnidadeId, DateTimeOffset? De, DateTimeOffset? Ate, int? Pagina, int? Tamanho);

public sealed class FiltroRelatorioValidator : AbstractValidator<FiltroRelatorio>
{
    public FiltroRelatorioValidator()
    {
        RuleFor(x => x.Ate).GreaterThanOrEqualTo(x => x.De).WithMessage("'ate' deve ser igual ou posterior a 'de'.");
        RuleFor(x => x).Must(x => x.Ate.DayNumber - x.De.DayNumber <= 366).WithMessage("O período máximo é de 366 dias.");
    }
}

/// <summary>
/// Relatórios consolidados para a matriz. Gerentes veem apenas a própria unidade.
/// Vendas consideradas: pedidos com pagamento confirmado (Pago, EmPreparo, Pronto, Entregue).
/// Datas são interpretadas no horário de Brasília (referência da matriz).
/// </summary>
public sealed class RelatorioService(IAppDbContext db, IUsuarioAtual usuario, IValidator<FiltroRelatorio> validador)
{
    private static readonly StatusPedido[] Vendidos = [StatusPedido.Pago, StatusPedido.EmPreparo, StatusPedido.Pronto, StatusPedido.Entregue];
    private static readonly TimeSpan FusoMatriz = TimeSpan.FromHours(-3);

    public async Task<RelatorioVendasDto> VendasAsync(FiltroRelatorio filtro, AgrupamentoVendas agrupamento, CancellationToken ct)
    {
        var unidadeId = await ValidarEscopoAsync(filtro, ct);
        if (usuario.Perfil == Perfil.Gerente && agrupamento == AgrupamentoVendas.Regiao)
            throw new AcessoNegadoException("Gerentes não acessam o consolidado por região.");

        var pedidos = Consultar(filtro, unidadeId);

        List<LinhaVendasDto> linhas;
        switch (agrupamento)
        {
            case AgrupamentoVendas.Unidade:
            {
                var dados = await (
                    from p in pedidos
                    join u in db.Unidades on p.UnidadeId equals u.Id
                    group p by new { u.Id, u.Nome, u.Uf } into g
                    select new { g.Key.Id, g.Key.Nome, g.Key.Uf, Qtd = g.Count(), Fat = g.Sum(x => x.Total), Desc = g.Sum(x => x.DescontoNivel + x.DescontoCampanha + x.DescontoPontos) }
                ).ToListAsync(ct);
                linhas = dados.Select(d => Linha(d.Id.ToString(), $"{d.Nome} ({d.Uf})", d.Qtd, d.Fat, d.Desc)).ToList();
                break;
            }
            case AgrupamentoVendas.Regiao:
            {
                var dados = await (
                    from p in pedidos
                    join u in db.Unidades on p.UnidadeId equals u.Id
                    group p by u.Regiao into g
                    select new { Regiao = g.Key, Qtd = g.Count(), Fat = g.Sum(x => x.Total), Desc = g.Sum(x => x.DescontoNivel + x.DescontoCampanha + x.DescontoPontos) }
                ).ToListAsync(ct);
                linhas = dados.Select(d => Linha(d.Regiao.ToString(), d.Regiao.ToString(), d.Qtd, d.Fat, d.Desc)).ToList();
                break;
            }
            default:
            {
                var dados = await pedidos
                    .GroupBy(p => p.CanalPedido)
                    .Select(g => new { Canal = g.Key, Qtd = g.Count(), Fat = g.Sum(x => x.Total), Desc = g.Sum(x => x.DescontoNivel + x.DescontoCampanha + x.DescontoPontos) })
                    .ToListAsync(ct);
                linhas = dados.Select(d => Linha(d.Canal.ToString(), d.Canal.ToString(), d.Qtd, d.Fat, d.Desc)).ToList();
                break;
            }
        }

        linhas = linhas.OrderByDescending(l => l.Faturamento).ToList();
        var totalPedidos = linhas.Sum(l => l.Pedidos);
        var faturamento = linhas.Sum(l => l.Faturamento);
        return new RelatorioVendasDto(filtro.De, filtro.Ate, agrupamento, linhas, totalPedidos, faturamento,
            totalPedidos == 0 ? 0 : decimal.Round(faturamento / totalPedidos, 2));
    }

    public async Task<IReadOnlyList<ProdutoMaisVendidoDto>> ProdutosMaisVendidosAsync(FiltroRelatorio filtro, int top, CancellationToken ct)
    {
        var unidadeId = await ValidarEscopoAsync(filtro, ct);
        top = Math.Clamp(top, 1, 50);

        var dados = await Consultar(filtro, unidadeId)
            .SelectMany(p => p.Itens)
            .GroupBy(i => new { i.ProdutoId, i.NomeProduto })
            .Select(g => new { g.Key.ProdutoId, g.Key.NomeProduto, Qtd = g.Sum(i => i.Quantidade), Fat = g.Sum(i => i.PrecoUnitario * i.Quantidade) })
            .OrderByDescending(x => x.Qtd)
            .Take(top)
            .ToListAsync(ct);

        return dados.Select(d => new ProdutoMaisVendidoDto(d.ProdutoId, d.NomeProduto, d.Qtd, d.Fat)).ToList();
    }

    /// <summary>Contagem de operações sensíveis no período (cancelamentos, descontos, ajustes...).</summary>
    public async Task<IReadOnlyList<ResumoAuditoriaDto>> OperacoesSensiveisAsync(FiltroRelatorio filtro, CancellationToken ct)
    {
        var unidadeId = await ValidarEscopoAsync(filtro, ct);
        var (de, ate) = Intervalo(filtro);
        var consulta = db.Auditoria.AsNoTracking().Where(a => a.OcorridoEm >= de && a.OcorridoEm < ate);
        if (unidadeId is { } u) consulta = consulta.Where(a => a.UnidadeId == u);
        var dados = await consulta.GroupBy(a => a.Acao)
            .Select(g => new { Acao = g.Key, Ocorrencias = g.Count() })
            .OrderByDescending(r => r.Ocorrencias)
            .ToListAsync(ct);
        return dados.Select(d => new ResumoAuditoriaDto(d.Acao, d.Ocorrencias)).ToList();
    }

    public async Task<ResultadoPaginado<RegistroAuditoriaDto>> ListarAuditoriaAsync(FiltroAuditoria f, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        var (pagina, tamanho) = Paginacao.Normalizar(f.Pagina, f.Tamanho);
        var q = db.Auditoria.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.Acao)) q = q.Where(a => a.Acao == f.Acao);
        if (!string.IsNullOrWhiteSpace(f.Entidade)) q = q.Where(a => a.Entidade == f.Entidade);
        if (!string.IsNullOrWhiteSpace(f.EntidadeId)) q = q.Where(a => a.EntidadeId == f.EntidadeId);
        if (f.UnidadeId is { } u) q = q.Where(a => a.UnidadeId == u);
        if (f.De is { } de) q = q.Where(a => a.OcorridoEm >= de);
        if (f.Ate is { } ate) q = q.Where(a => a.OcorridoEm < ate);

        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(a => a.OcorridoEm)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(a => new RegistroAuditoriaDto(a.Id, a.OcorridoEm, a.UsuarioId, a.Perfil, a.UnidadeId, a.Acao, a.Entidade, a.EntidadeId, a.DetalhesJson, a.CorrelacaoId))
            .ToListAsync(ct);
        return new ResultadoPaginado<RegistroAuditoriaDto>(itens, pagina, tamanho, total);
    }

    private IQueryable<Pedido> Consultar(FiltroRelatorio filtro, Guid? unidadeId)
    {
        var (de, ate) = Intervalo(filtro);
        var q = db.Pedidos.AsNoTracking().Where(p => Vendidos.Contains(p.Status) && p.CriadoEm >= de && p.CriadoEm < ate);
        if (unidadeId is { } u) q = q.Where(p => p.UnidadeId == u);
        return q;
    }

    private async Task<Guid?> ValidarEscopoAsync(FiltroRelatorio filtro, CancellationToken ct)
    {
        await validador.ValidateAndThrowAsync(filtro, ct);
        usuario.GarantirPerfil(Perfil.AdminMatriz, Perfil.Gerente);
        if (usuario.Perfil == Perfil.Gerente)
        {
            if (filtro.UnidadeId is { } pedido && pedido != usuario.UnidadeId)
                throw new AcessoNegadoException("Gerentes só consultam relatórios da própria unidade.");
            return usuario.UnidadeId;
        }
        return filtro.UnidadeId;
    }

    private static (DateTimeOffset de, DateTimeOffset ate) Intervalo(FiltroRelatorio f) =>
        (new DateTimeOffset(f.De.ToDateTime(TimeOnly.MinValue), FusoMatriz),
         new DateTimeOffset(f.Ate.AddDays(1).ToDateTime(TimeOnly.MinValue), FusoMatriz));

    private static LinhaVendasDto Linha(string chave, string descricao, int pedidos, decimal faturamento, decimal descontos) =>
        new(chave, descricao, pedidos, faturamento, pedidos == 0 ? 0 : decimal.Round(faturamento / pedidos, 2), descontos);
}
