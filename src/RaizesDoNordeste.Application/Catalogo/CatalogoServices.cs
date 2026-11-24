using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Catalogo;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Identidade;

namespace RaizesDoNordeste.Application.Catalogo;

public sealed record ProdutoRequest(
    string Nome,
    string? Descricao,
    CategoriaProduto Categoria,
    decimal PrecoBase,
    bool RequerCozinhaCompleta,
    string? SazonalidadeInicio,
    string? SazonalidadeFim,
    bool Ativo = true);

public sealed record SazonalidadeDto(string Inicio, string Fim);

public sealed record ProdutoDto(
    Guid Id, string Nome, string? Descricao, CategoriaProduto Categoria, decimal PrecoBase,
    bool RequerCozinhaCompleta, SazonalidadeDto? Sazonalidade, bool Ativo);

public sealed record ConfigurarItemCardapioRequest(bool Disponivel, decimal? PrecoLocal, string? ObservacaoRegional);

public sealed record ItemCardapioDto(
    Guid ProdutoId, string Nome, string? Descricao, CategoriaProduto Categoria, decimal Preco,
    string? ObservacaoRegional, bool Sazonal, bool Esgotado);

public sealed record CardapioDto(Guid UnidadeId, string Unidade, bool AbertaAgora, DateOnly DataLocal, IReadOnlyList<ItemCardapioDto> Itens);

public sealed class ProdutoRequestValidator : AbstractValidator<ProdutoRequest>
{
    public ProdutoRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Descricao).MaximumLength(500);
        RuleFor(x => x.Categoria).IsInEnum();
        RuleFor(x => x.PrecoBase).GreaterThan(0).LessThanOrEqualTo(1000).PrecisionScale(10, 2, true);
        RuleFor(x => x).Must(x => (x.SazonalidadeInicio is null) == (x.SazonalidadeFim is null))
            .WithMessage("Informe início e fim da sazonalidade, ou nenhum dos dois.");
        RuleFor(x => x.SazonalidadeInicio).Matches(@"^\d{2}-\d{2}$").When(x => x.SazonalidadeInicio is not null);
        RuleFor(x => x.SazonalidadeFim).Matches(@"^\d{2}-\d{2}$").When(x => x.SazonalidadeFim is not null);
    }
}

public sealed class ConfigurarItemCardapioValidator : AbstractValidator<ConfigurarItemCardapioRequest>
{
    public ConfigurarItemCardapioValidator()
    {
        RuleFor(x => x.PrecoLocal).GreaterThan(0).LessThanOrEqualTo(1000).When(x => x.PrecoLocal is not null);
        RuleFor(x => x.ObservacaoRegional).MaximumLength(300);
    }
}

public sealed class ProdutoService(IAppDbContext db, IUsuarioAtual usuario, IValidator<ProdutoRequest> validador)
{
    public async Task<IReadOnlyList<ProdutoDto>> ListarAsync(CategoriaProduto? categoria, CancellationToken ct)
    {
        var consulta = db.Produtos.AsNoTracking();
        if (!usuario.EhAdmin()) consulta = consulta.Where(p => p.Ativo);
        if (categoria is not null) consulta = consulta.Where(p => p.Categoria == categoria);
        var produtos = await consulta.OrderBy(p => p.Categoria).ThenBy(p => p.Nome).ToListAsync(ct);
        return produtos.Select(ParaDto).ToList();
    }

    public async Task<ProdutoDto> ObterAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Produtos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null || (!p.Ativo && !usuario.EhAdmin())) throw new NaoEncontradoException("Produto");
        return ParaDto(p);
    }

    public async Task<ProdutoDto> CriarAsync(ProdutoRequest req, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        await validador.ValidateAndThrowAsync(req, ct);
        var produto = new Produto(req.Nome, req.Descricao, req.Categoria, req.PrecoBase, req.RequerCozinhaCompleta, Janela(req));
        if (!req.Ativo) produto.Atualizar(req.Nome, req.Descricao, req.Categoria, req.PrecoBase, req.RequerCozinhaCompleta, Janela(req), false);
        db.Produtos.Add(produto);
        await db.SaveChangesAsync(ct);
        return ParaDto(produto);
    }

    public async Task<ProdutoDto> AtualizarAsync(Guid id, ProdutoRequest req, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        await validador.ValidateAndThrowAsync(req, ct);
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NaoEncontradoException("Produto");
        produto.Atualizar(req.Nome, req.Descricao, req.Categoria, req.PrecoBase, req.RequerCozinhaCompleta, Janela(req), req.Ativo);
        await db.SaveChangesAsync(ct);
        return ParaDto(produto);
    }

    private static JanelaSazonal? Janela(ProdutoRequest r) =>
        r.SazonalidadeInicio is null || r.SazonalidadeFim is null
            ? null
            : new JanelaSazonal(JanelaSazonal.ParseMesDia(r.SazonalidadeInicio), JanelaSazonal.ParseMesDia(r.SazonalidadeFim));

    internal static ProdutoDto ParaDto(Produto p) => new(
        p.Id, p.Nome, p.Descricao, p.Categoria, p.PrecoBase, p.RequerCozinhaCompleta,
        p.Sazonalidade is { } s ? new SazonalidadeDto(JanelaSazonal.FormatarMesDia(s.InicioMmdd), JanelaSazonal.FormatarMesDia(s.FimMmdd)) : null,
        p.Ativo);
}

public sealed class CardapioService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    IAuditoria auditoria,
    TimeProvider relogio,
    IValidator<ConfigurarItemCardapioRequest> validador)
{
    /// <summary>
    /// Cardápio da unidade: somente produtos ativos, liberados pela unidade, compatíveis com o
    /// tipo de cozinha e dentro da janela sazonal (na data local da unidade). Itens sem saldo
    /// aparecem como esgotados para o cliente saber que existem, mas não podem ser pedidos.
    /// </summary>
    public async Task<CardapioDto> ObterAsync(Guid unidadeId, CancellationToken ct)
    {
        var unidade = await db.Unidades.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unidadeId && u.Ativa, ct)
            ?? throw new NaoEncontradoException("Unidade");
        var agora = relogio.GetUtcNow();
        var dataLocal = unidade.DataLocal(agora);

        var itens = await (
            from ic in db.ItensCardapio.AsNoTracking()
            join p in db.Produtos.AsNoTracking() on ic.ProdutoId equals p.Id
            join e in db.Estoques.AsNoTracking().Where(e => e.UnidadeId == unidadeId) on ic.ProdutoId equals e.ProdutoId into est
            from e in est.DefaultIfEmpty()
            where ic.UnidadeId == unidadeId && ic.Disponivel && p.Ativo
            select new { Item = ic, Produto = p, Disponivel = e == null ? 0 : e.Quantidade - e.Reservada }
        ).ToListAsync(ct);

        var resultado = itens
            .Where(x => x.Produto.CompativelCom(unidade.Tipo) && x.Produto.DisponivelNaData(dataLocal))
            .OrderBy(x => x.Produto.Categoria).ThenBy(x => x.Produto.Nome)
            .Select(x => new ItemCardapioDto(
                x.Produto.Id, x.Produto.Nome, x.Produto.Descricao, x.Produto.Categoria,
                x.Item.PrecoEfetivo(x.Produto), x.Item.ObservacaoRegional,
                x.Produto.Sazonalidade is not null, x.Disponivel <= 0))
            .ToList();

        return new CardapioDto(unidade.Id, unidade.Nome, unidade.EstaAberta(agora), dataLocal, resultado);
    }

    public async Task<ItemCardapioDto> ConfigurarItemAsync(Guid unidadeId, Guid produtoId, ConfigurarItemCardapioRequest req, CancellationToken ct)
    {
        usuario.GarantirAcessoUnidade(unidadeId, Perfil.Gerente);
        await validador.ValidateAndThrowAsync(req, ct);

        var unidade = await db.Unidades.FirstOrDefaultAsync(u => u.Id == unidadeId, ct) ?? throw new NaoEncontradoException("Unidade");
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId, ct) ?? throw new NaoEncontradoException("Produto");
        if (req.Disponivel && !produto.CompativelCom(unidade.Tipo))
            throw new DomainException("PRODUTO_INCOMPATIVEL_UNIDADE", "Este produto exige cozinha completa e a unidade opera com cozinha reduzida.");

        var item = await db.ItensCardapio.FirstOrDefaultAsync(i => i.UnidadeId == unidadeId && i.ProdutoId == produtoId, ct);
        if (item is null)
        {
            item = new ItemCardapio(unidadeId, produtoId);
            db.ItensCardapio.Add(item);
        }

        var precoAnterior = item.PrecoLocal;
        item.Configurar(req.Disponivel, req.PrecoLocal, req.ObservacaoRegional);
        if (precoAnterior != item.PrecoLocal)
        {
            auditoria.Registrar(AcoesAuditoria.PrecoLocalAlterado, nameof(ItemCardapio), $"{unidadeId}:{produtoId}",
                new { Produto = produto.Nome, PrecoBase = produto.PrecoBase, De = precoAnterior, Para = item.PrecoLocal }, unidadeId);
        }

        await db.SaveChangesAsync(ct);

        var disponivel = await db.Estoques.Where(e => e.UnidadeId == unidadeId && e.ProdutoId == produtoId)
            .Select(e => (int?)(e.Quantidade - e.Reservada)).FirstOrDefaultAsync(ct) ?? 0;
        return new ItemCardapioDto(produto.Id, produto.Nome, produto.Descricao, produto.Categoria, item.PrecoEfetivo(produto),
            item.ObservacaoRegional, produto.Sazonalidade is not null, disponivel <= 0);
    }
}
