using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Campanhas;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Estoques;
using RaizesDoNordeste.Application.Fidelidade;
using RaizesDoNordeste.Application.Pagamentos;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.Application.Pedidos;

public sealed class PedidoService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    IAuditoria auditoria,
    TimeProvider relogio,
    IOptions<PedidoOpcoes> opcoes,
    EstoqueOperacoes estoque,
    FidelidadeOperacoes fidelidade,
    ConsultasCliente consultas,
    CampanhaService campanhas,
    EstornoPagamentos estornos,
    IValidator<CriarPedidoRequest> validadorCriacao,
    IValidator<CancelarPedidoRequest> validadorCancelamento)
{
    /// <summary>Quem pode mover o pedido para cada status (Pago é definido pelo gateway; Cancelado tem endpoint próprio).</summary>
    private static readonly Dictionary<StatusPedido, Perfil[]> PerfisPorStatus = new()
    {
        [StatusPedido.EmPreparo] = [Perfil.Cozinha, Perfil.Atendente, Perfil.Gerente],
        [StatusPedido.Pronto] = [Perfil.Cozinha, Perfil.Atendente, Perfil.Gerente],
        [StatusPedido.Entregue] = [Perfil.Atendente, Perfil.Gerente]
    };

    public async Task<ResultadoCriacaoPedido> CriarAsync(CriarPedidoRequest req, string? chaveIdempotencia, CancellationToken ct)
    {
        await validadorCriacao.ValidateAndThrowAsync(req, ct);
        if (string.IsNullOrWhiteSpace(chaveIdempotencia) || chaveIdempotencia.Length > 100)
            throw new ValidationException("O header Idempotency-Key é obrigatório (até 100 caracteres) para evitar pedidos duplicados.");

        var usuarioId = usuario.IdObrigatorio();
        var canal = DefinirCanal(req);

        // Re-envio da mesma requisição (ex.: rede instável no totem): devolve o pedido já criado.
        var existente = await BuscarPorChaveAsync(usuarioId, chaveIdempotencia, ct);
        if (existente is not null) return new ResultadoCriacaoPedido(existente, false);

        var agora = relogio.GetUtcNow();
        var unidade = await db.Unidades.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.UnidadeId && u.Ativa, ct)
            ?? throw new NaoEncontradoException("Unidade");
        if (canal is CanalPedido.Totem or CanalPedido.Balcao)
            usuario.GarantirAcessoUnidade(unidade.Id);
        if (!unidade.EstaAberta(agora))
            throw new DomainException("UNIDADE_FECHADA", "A unidade está fechada no momento.");

        var itens = await MontarItensAsync(unidade, req.Itens, agora, ct);
        var subtotal = itens.Sum(i => i.PrecoUnitario * i.Quantidade);

        Guid? clienteId = usuario.Perfil == Perfil.Cliente ? usuarioId : null;
        decimal percentualNivel = 0, percentualCampanha = 0;
        var saldoPontos = 0;
        if (clienteId is { } cid)
        {
            var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct)
                ?? throw new NaoEncontradoException("Cliente");
            saldoPontos = cliente.SaldoPontos;
            if (await consultas.ConsentimentoAtivoAsync(cid, TipoConsentimento.ProgramaFidelidade, ct))
            {
                var entregues = await consultas.PedidosEntreguesNaJanelaAsync(cid, agora, ct);
                percentualNivel = RegrasFidelidade.PercentualDesconto(RegrasFidelidade.CalcularNivel(entregues));
            }
            else if (req.PontosResgatar > 0)
            {
                throw new DomainException("FIDELIDADE_SEM_CONSENTIMENTO", "Para usar pontos é preciso participar do programa de fidelidade.");
            }

            if (req.CampanhaId is { } campanhaId)
                percentualCampanha = await campanhas.PercentualParaPedidoAsync(campanhaId, cid, unidade.Regiao, ct);
        }
        else if (req.PontosResgatar > 0 || req.CampanhaId is not null)
        {
            throw new DomainException("CLIENTE_OBRIGATORIO", "Pontos e campanhas exigem cliente identificado.");
        }

        var precificacao = CalculadoraPedido.Calcular(subtotal, percentualNivel, percentualCampanha, req.PontosResgatar, saldoPontos);
        var prazo = TimeSpan.FromMinutes(opcoes.Value.PrazoPagamentoMinutos);
        var movimentados = itens.Select(i => new ItemMovimentado(i.ProdutoId, i.NomeProduto, i.Quantidade)).ToList();

        try
        {
            var pedido = await db.EmTransacaoAsync(async c =>
            {
                var p = Pedido.Criar(unidade.Id, clienteId, canal, usuarioId, chaveIdempotencia, itens, precificacao,
                    req.CampanhaId, req.NomeChamada, req.Observacao, agora, prazo);

                await estoque.ReservarAsync(unidade.Id, movimentados, c);
                if (clienteId is { } id && p.PontosResgatados > 0)
                    await fidelidade.DebitarAsync(id, p.PontosResgatados, p.Id, c);

                db.Pedidos.Add(p);
                auditoria.Registrar(AcoesAuditoria.PedidoCriado, nameof(Pedido), p.Id,
                    new { p.CanalPedido, p.Total, Itens = p.Itens.Count, p.ClienteId }, p.UnidadeId);
                if (p.DescontoTotal > 0)
                {
                    auditoria.Registrar(AcoesAuditoria.DescontoAplicado, nameof(Pedido), p.Id, new
                    {
                        p.Subtotal, p.DescontoNivel, p.DescontoCampanha, p.DescontoPontos, p.PontosResgatados, p.CampanhaId, p.Total
                    }, p.UnidadeId);
                }
                await db.SaveChangesAsync(c);
                return p;
            }, ct);

            return new ResultadoCriacaoPedido(ParaDto(pedido, []), true);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Pedidos_CriadoPorUsuarioId_ChaveIdempotencia") == true)
        {
            // Duas requisições simultâneas com a mesma chave: a segunda devolve o pedido da primeira.
            db.LimparRastreamento();
            var duplicado = await BuscarPorChaveAsync(usuarioId, chaveIdempotencia, ct);
            if (duplicado is not null) return new ResultadoCriacaoPedido(duplicado, false);
            throw;
        }
    }

    public async Task<PedidoDto> ObterAsync(Guid id, CancellationToken ct)
    {
        var pedido = await db.Pedidos.AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Historico)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NaoEncontradoException("Pedido");
        GarantirAcessoAoPedido(pedido);
        var pagamentos = await db.Pagamentos.AsNoTracking().Where(p => p.PedidoId == id).OrderBy(p => p.CriadoEm).ToListAsync(ct);
        return ParaDto(pedido, pagamentos);
    }

    public async Task<ResultadoPaginado<PedidoResumoDto>> ListarAsync(FiltroPedidos filtro, CancellationToken ct)
    {
        var usuarioId = usuario.IdObrigatorio();
        var (pagina, tamanho) = Paginacao.Normalizar(filtro.Pagina, filtro.Tamanho);
        var consulta = db.Pedidos.AsNoTracking();

        consulta = usuario.Perfil switch
        {
            Perfil.Cliente => consulta.Where(p => p.ClienteId == usuarioId),
            Perfil.Totem => consulta.Where(p => p.CriadoPorUsuarioId == usuarioId),
            Perfil.AdminMatriz => filtro.UnidadeId is { } u ? consulta.Where(p => p.UnidadeId == u) : consulta,
            _ => consulta.Where(p => p.UnidadeId == usuario.UnidadeId)
        };
        if (filtro.Status is { } status) consulta = consulta.Where(p => p.Status == status);
        if (filtro.CanalPedido is { } canalFiltro) consulta = consulta.Where(p => p.CanalPedido == canalFiltro);
        if (filtro.De is { } de) consulta = consulta.Where(p => p.CriadoEm >= de);
        if (filtro.Ate is { } ate) consulta = consulta.Where(p => p.CriadoEm < ate);

        var total = await consulta.CountAsync(ct);
        var itens = await consulta
            .OrderByDescending(p => p.CriadoEm)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(p => new PedidoResumoDto(p.Id, p.UnidadeId, p.CanalPedido, p.Status, p.NomeChamada, p.Total,
                p.Itens.Sum(i => i.Quantidade), p.CriadoEm))
            .ToListAsync(ct);
        return new ResultadoPaginado<PedidoResumoDto>(itens, pagina, tamanho, total);
    }

    public async Task<PedidoDto> AtualizarStatusAsync(Guid id, AtualizarStatusPedidoRequest req, CancellationToken ct)
    {
        if (!PerfisPorStatus.TryGetValue(req.Status, out var perfis))
            throw new DomainException("STATUS_NAO_PERMITIDO",
                "Este endpoint aceita apenas EmPreparo, Pronto ou Entregue. Pagamento é confirmado pelo gateway e cancelamento tem endpoint próprio.");

        var usuarioId = usuario.IdObrigatorio();
        var agora = relogio.GetUtcNow();

        await db.EmTransacaoAsync(async c =>
        {
            var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id, c)
                ?? throw new NaoEncontradoException("Pedido");
            GarantirAcessoAoPedido(pedido);
            if (usuario.Perfil is not { } perfil || !perfis.Contains(perfil) || usuario.UnidadeId != pedido.UnidadeId)
                throw new AcessoNegadoException($"Seu perfil não pode mover pedidos para {req.Status}.");

            var anterior = pedido.Status;
            switch (req.Status)
            {
                case StatusPedido.EmPreparo: pedido.IniciarPreparo(usuarioId, agora); break;
                case StatusPedido.Pronto: pedido.MarcarPronto(usuarioId, agora); break;
                case StatusPedido.Entregue:
                    pedido.Entregar(usuarioId, agora);
                    await CreditarPontosAsync(pedido, c);
                    break;
            }
            auditoria.Registrar(AcoesAuditoria.PedidoStatusAlterado, nameof(Pedido), pedido.Id,
                new { De = anterior, Para = pedido.Status }, pedido.UnidadeId);
            await db.SaveChangesAsync(c);
            return true;
        }, ct);

        db.LimparRastreamento();
        return await ObterAsync(id, ct);
    }

    public async Task<PedidoDto> CancelarAsync(Guid id, CancelarPedidoRequest req, CancellationToken ct)
    {
        await validadorCancelamento.ValidateAndThrowAsync(req, ct);
        var usuarioId = usuario.IdObrigatorio();
        if (usuario.Perfil == Perfil.Cozinha) throw new AcessoNegadoException("A cozinha não cancela pedidos; acione a gerência.");
        var autorizadoPelaGerencia = usuario.Perfil is Perfil.Gerente or Perfil.AdminMatriz;
        var agora = relogio.GetUtcNow();

        var pagamentoParaEstornar = await db.EmTransacaoAsync(async c =>
        {
            var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id, c)
                ?? throw new NaoEncontradoException("Pedido");
            GarantirAcessoAoPedido(pedido);

            var anterior = pedido.Status;
            pedido.Cancelar(req.Motivo, usuarioId, autorizadoPelaGerencia, agora);
            var itens = pedido.Itens.Select(i => new ItemMovimentado(i.ProdutoId, i.NomeProduto, i.Quantidade)).ToList();

            switch (anterior)
            {
                case StatusPedido.AguardandoPagamento:
                    await estoque.LiberarReservaAsync(pedido.UnidadeId, itens, c);
                    break;
                case StatusPedido.Pago:
                    await estoque.DevolverAsync(pedido.UnidadeId, pedido.Id, usuarioId, itens, c);
                    break;
                // EmPreparo: os insumos já foram usados; nada volta ao estoque (perda registrada na auditoria).
            }

            if (pedido.ClienteId is { } clienteId && pedido.PontosResgatados > 0)
                await fidelidade.EstornarResgateAsync(clienteId, pedido.PontosResgatados, pedido.Id, c);

            Guid? pagamentoAprovado = anterior is StatusPedido.Pago or StatusPedido.EmPreparo
                ? await db.Pagamentos.Where(p => p.PedidoId == pedido.Id && p.Status == StatusPagamento.Aprovado)
                    .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(c)
                : null;

            auditoria.Registrar(AcoesAuditoria.PedidoCancelado, nameof(Pedido), pedido.Id, new
            {
                StatusAnterior = anterior,
                req.Motivo,
                pedido.Total,
                PerdaDeInsumos = anterior == StatusPedido.EmPreparo,
                EstornoNecessario = pagamentoAprovado is not null
            }, pedido.UnidadeId);

            await db.SaveChangesAsync(c);
            return pagamentoAprovado;
        }, ct);

        // A chamada ao gateway fica fora da transação: não seguramos locks do banco esperando um serviço externo.
        if (pagamentoParaEstornar is { } pagamentoId)
            await estornos.EstornarAsync(pagamentoId, $"Pedido cancelado: {req.Motivo}", ct);

        db.LimparRastreamento();
        return await ObterAsync(id, ct);
    }

    private async Task CreditarPontosAsync(Pedido pedido, CancellationToken ct)
    {
        if (pedido.ClienteId is not { } clienteId) return;
        if (!await consultas.ConsentimentoAtivoAsync(clienteId, TipoConsentimento.ProgramaFidelidade, ct)) return;
        await fidelidade.CreditarAsync(clienteId, RegrasFidelidade.PontosGanhos(pedido.Total), pedido.Id, ct);
    }

    /// <summary>
    /// O canal é informado no pedido (dado de domínio) e conferido contra o perfil autenticado, para impedir
    /// que um cliente se passe por totem/balcão (ou vice-versa) e distorça a rastreabilidade por canal.
    /// </summary>
    private CanalPedido DefinirCanal(CriarPedidoRequest req)
    {
        var canal = req.CanalPedido!.Value;
        CanalPedido[] permitidos = usuario.Perfil switch
        {
            Perfil.Cliente => [CanalPedido.App, CanalPedido.Web, CanalPedido.Pickup],
            Perfil.Totem => [CanalPedido.Totem],
            Perfil.Atendente or Perfil.Gerente => [CanalPedido.Balcao],
            _ => throw new AcessoNegadoException("Seu perfil não pode registrar pedidos.")
        };
        if (!permitidos.Contains(canal))
            throw new DomainException("CANAL_INCOMPATIVEL",
                $"O canal {canal} não é permitido para o perfil {usuario.Perfil}. Permitidos: {string.Join(", ", permitidos)}.");
        return canal;
    }

    private async Task<List<ItemNovoPedido>> MontarItensAsync(
        Domain.Unidades.Unidade unidade, IReadOnlyList<ItemPedidoRequest> solicitados, DateTimeOffset agora, CancellationToken ct)
    {
        var ids = solicitados.Select(i => i.ProdutoId).ToList();
        var existentes = await db.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        if (ids.FirstOrDefault(id => !existentes.Contains(id)) is var inexistente && inexistente != Guid.Empty)
            throw new NaoEncontradoException($"Produto {inexistente}");

        var cardapio = await (
            from ic in db.ItensCardapio.AsNoTracking()
            join p in db.Produtos.AsNoTracking() on ic.ProdutoId equals p.Id
            where ic.UnidadeId == unidade.Id && ids.Contains(ic.ProdutoId)
            select new { Item = ic, Produto = p }).ToListAsync(ct);

        var dataLocal = unidade.DataLocal(agora);
        var itens = new List<ItemNovoPedido>(solicitados.Count);
        foreach (var s in solicitados)
        {
            var x = cardapio.FirstOrDefault(c => c.Produto.Id == s.ProdutoId);
            if (x is null || !x.Item.Disponivel || !x.Produto.Ativo || !x.Produto.CompativelCom(unidade.Tipo))
                throw new DomainException("PRODUTO_INDISPONIVEL", $"O produto {s.ProdutoId} não está disponível nesta unidade.");
            if (!x.Produto.DisponivelNaData(dataLocal))
                throw new DomainException("PRODUTO_FORA_DE_TEMPORADA", $"'{x.Produto.Nome}' é sazonal e não está disponível nesta época.");
            itens.Add(new ItemNovoPedido(x.Produto.Id, x.Produto.Nome, x.Item.PrecoEfetivo(x.Produto), s.Quantidade));
        }
        return itens;
    }

    private async Task<PedidoDto?> BuscarPorChaveAsync(Guid usuarioId, string chave, CancellationToken ct)
    {
        var id = await db.Pedidos.AsNoTracking()
            .Where(p => p.CriadoPorUsuarioId == usuarioId && p.ChaveIdempotencia == chave)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await ObterAsync(id.Value, ct);
    }

    /// <summary>
    /// Quem não tem acesso recebe 404 (e não 403) para não revelar a existência de pedidos de terceiros.
    /// </summary>
    private void GarantirAcessoAoPedido(Pedido p)
    {
        var permitido = usuario.Perfil switch
        {
            Perfil.AdminMatriz => true,
            Perfil.Cliente => p.ClienteId == usuario.Id,
            Perfil.Totem => p.CriadoPorUsuarioId == usuario.Id,
            Perfil.Atendente or Perfil.Cozinha or Perfil.Gerente => p.UnidadeId == usuario.UnidadeId,
            _ => false
        };
        if (!permitido) throw new NaoEncontradoException("Pedido");
    }

    internal static PedidoDto ParaDto(Pedido p, IEnumerable<Pagamento> pagamentos) => new(
        p.Id, p.UnidadeId, p.ClienteId, p.CanalPedido, p.Status, p.NomeChamada, p.Observacao,
        p.Subtotal, p.DescontoNivel, p.DescontoCampanha, p.DescontoPontos, p.Total, p.PontosResgatados, p.CampanhaId,
        p.MotivoCancelamento, p.CriadoEm, p.AtualizadoEm, p.ExpiraEm,
        p.Itens.Select(i => new PedidoItemDto(i.ProdutoId, i.NomeProduto, i.PrecoUnitario, i.Quantidade, i.Subtotal)).ToList(),
        p.Historico.OrderBy(h => h.OcorridoEm).Select(h => new HistoricoStatusDto(h.De, h.Para, h.UsuarioId, h.Motivo, h.OcorridoEm)).ToList(),
        pagamentos.Select(PagamentoService.ParaDto).ToList());
}
