using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.Application.Fidelidade;

public sealed record MovimentacaoPontosDto(TipoMovimentacaoPontos Tipo, int Pontos, Guid? PedidoId, DateTimeOffset OcorridoEm);

public sealed record FidelidadeDto(
    bool ParticipaDoPrograma,
    int SaldoPontos,
    decimal ValorEmReais,
    NivelFidelidade Nivel,
    decimal PercentualDescontoNivel,
    int PedidosNosUltimos90Dias,
    NivelFidelidade? ProximoNivel,
    int? PedidosParaProximoNivel,
    IReadOnlyList<MovimentacaoPontosDto> UltimasMovimentacoes);

/// <summary>Consultas reutilizadas por pedidos, campanhas e fidelidade.</summary>
public sealed class ConsultasCliente(IAppDbContext db)
{
    public async Task<bool> ConsentimentoAtivoAsync(Guid clienteId, TipoConsentimento tipo, CancellationToken ct) =>
        await db.Consentimentos.AsNoTracking()
            .Where(c => c.ClienteId == clienteId && c.Tipo == tipo)
            .OrderByDescending(c => c.RegistradoEm)
            .Select(c => (bool?)c.Concedido)
            .FirstOrDefaultAsync(ct) ?? false;

    public async Task<int> PedidosEntreguesNaJanelaAsync(Guid clienteId, DateTimeOffset agora, CancellationToken ct)
    {
        var desde = agora.AddDays(-RegrasFidelidade.JanelaFrequenciaDias);
        return await db.Pedidos.CountAsync(p => p.ClienteId == clienteId && p.Status == StatusPedido.Entregue && p.CriadoEm >= desde, ct);
    }
}

/// <summary>Movimentação de pontos com UPDATE atômico no saldo + lançamento no extrato.</summary>
public sealed class FidelidadeOperacoes(IAppDbContext db, TimeProvider relogio)
{
    public async Task DebitarAsync(Guid clienteId, int pontos, Guid pedidoId, CancellationToken ct)
    {
        if (pontos <= 0) return;
        var afetadas = await db.Clientes
            .Where(c => c.Id == clienteId && c.SaldoPontos >= pontos && !c.Anonimizado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SaldoPontos, c => c.SaldoPontos - pontos), ct);
        if (afetadas == 0) throw new DomainException("PONTOS_INSUFICIENTES", "Saldo de pontos insuficiente.");
        db.MovimentacoesPontos.Add(new MovimentacaoPontos(clienteId, TipoMovimentacaoPontos.Debito, pontos, pedidoId, relogio.GetUtcNow()));
    }

    public async Task CreditarAsync(Guid clienteId, int pontos, Guid pedidoId, CancellationToken ct)
    {
        if (pontos <= 0) return;
        var agora = relogio.GetUtcNow();
        var afetadas = await db.Clientes
            .Where(c => c.Id == clienteId && !c.Anonimizado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SaldoPontos, c => c.SaldoPontos + pontos), ct);
        if (afetadas == 0) return;
        db.MovimentacoesPontos.Add(new MovimentacaoPontos(clienteId, TipoMovimentacaoPontos.Credito, pontos, pedidoId, agora));
    }

    /// <summary>Devolve pontos resgatados em um pedido cancelado ou expirado.</summary>
    public async Task EstornarResgateAsync(Guid clienteId, int pontos, Guid pedidoId, CancellationToken ct)
    {
        if (pontos <= 0) return;
        var afetadas = await db.Clientes
            .Where(c => c.Id == clienteId && !c.Anonimizado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SaldoPontos, c => c.SaldoPontos + pontos), ct);
        if (afetadas == 0) return;
        db.MovimentacoesPontos.Add(new MovimentacaoPontos(clienteId, TipoMovimentacaoPontos.Estorno, pontos, pedidoId, relogio.GetUtcNow()));
    }
}

public sealed class FidelidadeService(IAppDbContext db, IUsuarioAtual usuario, ConsultasCliente consultas, TimeProvider relogio)
{
    public async Task<FidelidadeDto> ObterResumoAsync(CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.Cliente);
        var clienteId = usuario.IdObrigatorio();
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, ct)
            ?? throw new NaoEncontradoException("Cliente");

        var agora = relogio.GetUtcNow();
        var participa = await consultas.ConsentimentoAtivoAsync(clienteId, TipoConsentimento.ProgramaFidelidade, ct);
        var pedidos = await consultas.PedidosEntreguesNaJanelaAsync(clienteId, agora, ct);
        var nivel = RegrasFidelidade.CalcularNivel(pedidos);

        (NivelFidelidade? proximo, int? faltam) = nivel switch
        {
            NivelFidelidade.Bronze => (NivelFidelidade.Prata, RegrasFidelidade.PedidosParaPrata - pedidos),
            NivelFidelidade.Prata => (NivelFidelidade.Ouro, RegrasFidelidade.PedidosParaOuro - pedidos),
            _ => ((NivelFidelidade?)null, (int?)null)
        };

        var movimentacoes = await db.MovimentacoesPontos.AsNoTracking()
            .Where(m => m.ClienteId == clienteId)
            .OrderByDescending(m => m.OcorridoEm)
            .Take(20)
            .Select(m => new MovimentacaoPontosDto(m.Tipo, m.Pontos, m.PedidoId, m.OcorridoEm))
            .ToListAsync(ct);

        return new FidelidadeDto(participa, cliente.SaldoPontos, cliente.SaldoPontos * RegrasFidelidade.ValorPorPonto, nivel,
            participa ? RegrasFidelidade.PercentualDesconto(nivel) : 0, pedidos, proximo, faltam, movimentacoes);
    }
}
