using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Estoques;
using RaizesDoNordeste.Domain.Identidade;

namespace RaizesDoNordeste.Application.Estoques;

public enum TipoMovimentacaoManual { Entrada, Ajuste, Perda }

public sealed record MovimentarEstoqueRequest(Guid ProdutoId, TipoMovimentacaoManual Tipo, int Quantidade, string? Motivo);

public sealed record DefinirEstoqueMinimoRequest(int EstoqueMinimo);

public sealed record ItemEstoqueDto(Guid ProdutoId, string Produto, int Quantidade, int Reservada, int Disponivel, int Minimo, bool AbaixoDoMinimo);

public sealed class MovimentarEstoqueValidator : AbstractValidator<MovimentarEstoqueRequest>
{
    public MovimentarEstoqueValidator()
    {
        RuleFor(x => x.ProdutoId).NotEmpty();
        RuleFor(x => x.Tipo).IsInEnum();
        RuleFor(x => x.Quantidade).NotEqual(0).InclusiveBetween(-10_000, 10_000);
        RuleFor(x => x.Quantidade).GreaterThan(0).When(x => x.Tipo != TipoMovimentacaoManual.Ajuste)
            .WithMessage("Entrada e perda devem ter quantidade positiva.");
        RuleFor(x => x.Motivo).NotEmpty().When(x => x.Tipo != TipoMovimentacaoManual.Entrada)
            .WithMessage("Ajustes e perdas exigem motivo (auditoria).");
        RuleFor(x => x.Motivo).MaximumLength(300);
    }
}

public sealed class EstoqueService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    IAuditoria auditoria,
    TimeProvider relogio,
    IValidator<MovimentarEstoqueRequest> validador)
{
    public async Task<IReadOnlyList<ItemEstoqueDto>> ListarAsync(Guid unidadeId, bool apenasAbaixoDoMinimo, CancellationToken ct)
    {
        usuario.GarantirAcessoUnidade(unidadeId, Perfil.Gerente, Perfil.Atendente, Perfil.Cozinha);
        var estoques = db.Estoques.AsNoTracking().Where(e => e.UnidadeId == unidadeId);
        if (apenasAbaixoDoMinimo) estoques = estoques.Where(e => e.Quantidade - e.Reservada <= e.Minimo);

        // Filtro e ordenação antes da projeção: o EF traduz para SQL; depois do "new Dto(...)" não conseguiria.
        return await (
            from e in estoques
            join p in db.Produtos.AsNoTracking() on e.ProdutoId equals p.Id
            orderby p.Nome
            select new ItemEstoqueDto(p.Id, p.Nome, e.Quantidade, e.Reservada, e.Quantidade - e.Reservada, e.Minimo,
                e.Quantidade - e.Reservada <= e.Minimo)
        ).ToListAsync(ct);
    }

    public async Task<ItemEstoqueDto> MovimentarAsync(Guid unidadeId, MovimentarEstoqueRequest req, CancellationToken ct)
    {
        usuario.GarantirAcessoUnidade(unidadeId, Perfil.Gerente);
        await validador.ValidateAndThrowAsync(req, ct);

        if (!await db.Unidades.AnyAsync(u => u.Id == unidadeId, ct)) throw new NaoEncontradoException("Unidade");
        var produto = await db.Produtos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ProdutoId, ct)
            ?? throw new NaoEncontradoException("Produto");

        var item = await db.Estoques.FirstOrDefaultAsync(e => e.UnidadeId == unidadeId && e.ProdutoId == req.ProdutoId, ct);
        if (item is null)
        {
            if (req.Tipo != TipoMovimentacaoManual.Entrada)
                throw new DomainException("ESTOQUE_INEXISTENTE", "Não há estoque deste produto na unidade; registre uma entrada primeiro.");
            item = new ItemEstoque(unidadeId, req.ProdutoId);
            db.Estoques.Add(item);
        }

        var anterior = item.Quantidade;
        (TipoMovimentacaoEstoque tipo, int efeito) = req.Tipo switch
        {
            TipoMovimentacaoManual.Entrada => (TipoMovimentacaoEstoque.Entrada, req.Quantidade),
            TipoMovimentacaoManual.Ajuste => (TipoMovimentacaoEstoque.Ajuste, req.Quantidade),
            _ => (TipoMovimentacaoEstoque.Perda, -req.Quantidade)
        };

        switch (req.Tipo)
        {
            case TipoMovimentacaoManual.Entrada: item.Entrada(req.Quantidade); break;
            case TipoMovimentacaoManual.Ajuste: item.Ajustar(req.Quantidade); break;
            default: item.RegistrarPerda(req.Quantidade); break;
        }

        db.MovimentacoesEstoque.Add(new MovimentacaoEstoque(unidadeId, req.ProdutoId, tipo, efeito, req.Motivo, usuario.Id, null, relogio.GetUtcNow()));

        if (req.Tipo != TipoMovimentacaoManual.Entrada)
        {
            auditoria.Registrar(AcoesAuditoria.EstoqueAjustado, nameof(ItemEstoque), $"{unidadeId}:{req.ProdutoId}",
                new { Produto = produto.Nome, Tipo = req.Tipo, De = anterior, Para = item.Quantidade, req.Motivo }, unidadeId);
        }

        // Concorrência otimista (rowversion): se outra operação alterou o saldo entre a leitura e a
        // gravação, o EF lança DbUpdateConcurrencyException e a API responde 409 para o usuário repetir.
        await db.SaveChangesAsync(ct);
        return ParaDto(item, produto.Nome);
    }

    public async Task<ItemEstoqueDto> DefinirMinimoAsync(Guid unidadeId, Guid produtoId, DefinirEstoqueMinimoRequest req, CancellationToken ct)
    {
        usuario.GarantirAcessoUnidade(unidadeId, Perfil.Gerente);
        var item = await db.Estoques.FirstOrDefaultAsync(e => e.UnidadeId == unidadeId && e.ProdutoId == produtoId, ct)
            ?? throw new NaoEncontradoException("Item de estoque");
        item.DefinirMinimo(req.EstoqueMinimo);
        await db.SaveChangesAsync(ct);
        var nome = await db.Produtos.Where(p => p.Id == produtoId).Select(p => p.Nome).FirstAsync(ct);
        return ParaDto(item, nome);
    }

    private static ItemEstoqueDto ParaDto(ItemEstoque e, string produto) =>
        new(e.ProdutoId, produto, e.Quantidade, e.Reservada, e.Disponivel, e.Minimo, e.AbaixoDoMinimo);
}

public sealed record ItemMovimentado(Guid ProdutoId, string NomeProduto, int Quantidade);

/// <summary>
/// Operações de estoque disparadas por pedidos. Cada uma é um UPDATE atômico com condição
/// (ex.: "reservar só se houver saldo"), executado dentro da transação do pedido. Isso evita
/// venda acima do saldo quando muitos clientes compram o mesmo item ao mesmo tempo, sem
/// bloqueios longos. Itens são processados em ordem de ProdutoId para evitar deadlocks.
/// </summary>
public sealed class EstoqueOperacoes(IAppDbContext db, TimeProvider relogio, ILogger<EstoqueOperacoes> logger)
{
    public async Task ReservarAsync(Guid unidadeId, IEnumerable<ItemMovimentado> itens, CancellationToken ct)
    {
        foreach (var item in itens.OrderBy(i => i.ProdutoId))
        {
            var q = item.Quantidade;
            var afetadas = await db.Estoques
                .Where(e => e.UnidadeId == unidadeId && e.ProdutoId == item.ProdutoId && e.Quantidade - e.Reservada >= q)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Reservada, e => e.Reservada + q), ct);
            if (afetadas == 0)
                throw new DomainException("ESTOQUE_INSUFICIENTE", $"Estoque insuficiente para '{item.NomeProduto}'.");
        }
    }

    public async Task LiberarReservaAsync(Guid unidadeId, IEnumerable<ItemMovimentado> itens, CancellationToken ct)
    {
        foreach (var item in itens.OrderBy(i => i.ProdutoId))
        {
            var q = item.Quantidade;
            await db.Estoques
                .Where(e => e.UnidadeId == unidadeId && e.ProdutoId == item.ProdutoId && e.Reservada >= q)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Reservada, e => e.Reservada - q), ct);
        }
    }

    /// <summary>Pagamento aprovado: a reserva vira saída definitiva.</summary>
    public async Task BaixarAsync(Guid unidadeId, Guid pedidoId, IEnumerable<ItemMovimentado> itens, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var lista = itens.OrderBy(i => i.ProdutoId).ToList();
        foreach (var item in lista)
        {
            var q = item.Quantidade;
            var afetadas = await db.Estoques
                .Where(e => e.UnidadeId == unidadeId && e.ProdutoId == item.ProdutoId && e.Reservada >= q && e.Quantidade >= q)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Quantidade, e => e.Quantidade - q)
                    .SetProperty(e => e.Reservada, e => e.Reservada - q), ct);
            if (afetadas == 0)
                throw new InvalidOperationException($"Reserva inconsistente para o produto {item.ProdutoId} no pedido {pedidoId}.");
            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque(unidadeId, item.ProdutoId, TipoMovimentacaoEstoque.SaidaVenda, -q, null, null, pedidoId, agora));
        }

        var ids = lista.Select(i => i.ProdutoId).ToList();
        var abaixo = await db.Estoques.AsNoTracking()
            .Where(e => e.UnidadeId == unidadeId && ids.Contains(e.ProdutoId) && e.Quantidade - e.Reservada <= e.Minimo)
            .Select(e => new { e.ProdutoId, Disponivel = e.Quantidade - e.Reservada, e.Minimo })
            .ToListAsync(ct);
        foreach (var e in abaixo)
            logger.LogWarning("Estoque mínimo atingido na unidade {UnidadeId}, produto {ProdutoId}: disponível {Disponivel}, mínimo {Minimo}",
                unidadeId, e.ProdutoId, e.Disponivel, e.Minimo);
    }

    /// <summary>Cancelamento antes do preparo, com pagamento já baixado: os itens voltam ao saldo.</summary>
    public async Task DevolverAsync(Guid unidadeId, Guid pedidoId, Guid? usuarioId, IEnumerable<ItemMovimentado> itens, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        foreach (var item in itens.OrderBy(i => i.ProdutoId))
        {
            var q = item.Quantidade;
            await db.Estoques
                .Where(e => e.UnidadeId == unidadeId && e.ProdutoId == item.ProdutoId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Quantidade, e => e.Quantidade + q), ct);
            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque(unidadeId, item.ProdutoId, TipoMovimentacaoEstoque.DevolucaoCancelamento, q,
                "Cancelamento antes do preparo", usuarioId, pedidoId, agora));
        }
    }
}
