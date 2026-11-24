using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Estoques;
using RaizesDoNordeste.Application.Fidelidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.Application.Pedidos;

/// <summary>
/// Expira pedidos não pagos no prazo, devolvendo o estoque reservado e os pontos resgatados.
/// Executado periodicamente por um BackgroundService (Infrastructure). Pedidos com pagamento
/// pendente recente são preservados, pois a confirmação do gateway pode estar a caminho.
/// </summary>
public sealed class ExpiracaoPedidosService(
    IAppDbContext db,
    TimeProvider relogio,
    IOptions<PedidoOpcoes> opcoes,
    EstoqueOperacoes estoque,
    FidelidadeOperacoes fidelidade,
    ILogger<ExpiracaoPedidosService> logger)
{
    public async Task<int> ExpirarVencidosAsync(int lote, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var limitePendente = agora.AddMinutes(-opcoes.Value.ToleranciaPagamentoPendenteMinutos);

        var ids = await db.Pedidos.AsNoTracking()
            .Where(p => p.Status == StatusPedido.AguardandoPagamento && p.ExpiraEm < agora)
            .Where(p => !db.Pagamentos.Any(pg => pg.PedidoId == p.Id && pg.Status == StatusPagamento.Pendente && pg.CriadoEm > limitePendente))
            .OrderBy(p => p.ExpiraEm)
            .Select(p => p.Id)
            .Take(lote)
            .ToListAsync(ct);

        var expirados = 0;
        foreach (var id in ids)
        {
            try
            {
                var ok = await db.EmTransacaoAsync(async c =>
                {
                    var pedido = await db.Pedidos.Include(p => p.Itens).FirstAsync(p => p.Id == id, c);
                    if (!pedido.PrazoPagamentoVencido(agora)) return false;

                    pedido.Expirar(agora);
                    await estoque.LiberarReservaAsync(pedido.UnidadeId,
                        pedido.Itens.Select(i => new ItemMovimentado(i.ProdutoId, i.NomeProduto, i.Quantidade)), c);
                    if (pedido.ClienteId is { } clienteId && pedido.PontosResgatados > 0)
                        await fidelidade.EstornarResgateAsync(clienteId, pedido.PontosResgatados, pedido.Id, c);
                    await db.SaveChangesAsync(c);
                    return true;
                }, ct);
                if (ok) expirados++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // O pedido mudou enquanto expirávamos (ex.: webhook de pagamento chegou). A outra operação prevalece.
                logger.LogInformation("Pedido {PedidoId} alterado concorrentemente; expiração ignorada.", id);
            }
            finally
            {
                db.LimparRastreamento();
            }
        }

        if (expirados > 0) logger.LogInformation("{Quantidade} pedido(s) expirado(s) por falta de pagamento.", expirados);
        return expirados;
    }
}
