using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Domain.Catalogo;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Estoques;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.Domain.Unidades;
using RaizesDoNordeste.Infrastructure.Persistencia;

namespace RaizesDoNordeste.Infrastructure.Seed;

public sealed class SeedOpcoes
{
    public const string Secao = "Seed";

    public bool AplicarMigracoes { get; set; } = true;

    /// <summary>Administrador inicial (bootstrap). Credenciais vêm de variáveis de ambiente.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminSenha { get; set; }

    /// <summary>Carrega unidades, cardápio, equipes, clientes e histórico de vendas para demonstração.</summary>
    public bool DadosDemonstracao { get; set; }

    /// <summary>Senha usada por todas as contas de demonstração.</summary>
    public string SenhaDemonstracao { get; set; } = "Raizes@2026";
}

public sealed class DataSeeder(AppDbContext db, ISenhaHasher hasher, TimeProvider relogio, IOptions<SeedOpcoes> opcoes, ILogger<DataSeeder> logger)
{
    public const string UnidadeAeroporto = "Raízes Aeroporto Guararapes";

    public async Task ExecutarAsync(CancellationToken ct = default)
    {
        var o = opcoes.Value;
        if (o.AplicarMigracoes)
        {
            await db.Database.MigrateAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(o.AdminEmail) && !string.IsNullOrWhiteSpace(o.AdminSenha))
        {
            var email = Usuario.NormalizarEmail(o.AdminEmail);
            if (!await db.Usuarios.AnyAsync(u => u.Email == email, ct))
            {
                db.Usuarios.Add(new Usuario("Administrador da Matriz", email, hasher.Gerar(o.AdminSenha), Perfil.AdminMatriz, null, relogio.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Administrador inicial criado.");
            }
        }

        if (o.DadosDemonstracao && !await db.Unidades.AnyAsync(ct))
        {
            await CarregarDemonstracaoAsync(o.SenhaDemonstracao, ct);
        }
    }

    private async Task CarregarDemonstracaoAsync(string senha, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var hash = hasher.Gerar(senha);

        var unidades = new List<(Unidade U, string Slug)>
        {
            (new Unidade("Raízes Boa Viagem", "Recife", "PE", Regiao.Nordeste, TipoUnidade.CozinhaCompleta, "America/Recife", new(6, 0), new(22, 0)), "boaviagem"),
            (new Unidade(UnidadeAeroporto, "Recife", "PE", Regiao.Nordeste, TipoUnidade.CozinhaReduzida, "America/Recife", new(0, 0), new(0, 0)), "aeroporto"),
            (new Unidade("Raízes Aldeota", "Fortaleza", "CE", Regiao.Nordeste, TipoUnidade.CozinhaReduzida, "America/Fortaleza", new(6, 0), new(20, 0)), "aldeota"),
            (new Unidade("Raízes Pelourinho", "Salvador", "BA", Regiao.Nordeste, TipoUnidade.CozinhaCompleta, "America/Bahia", new(6, 0), new(22, 0)), "pelourinho"),
            (new Unidade("Raízes Paulista", "São Paulo", "SP", Regiao.Sudeste, TipoUnidade.CozinhaCompleta, "America/Sao_Paulo", new(6, 0), new(23, 0)), "paulista"),
            (new Unidade("Raízes Asa Sul", "Brasília", "DF", Regiao.CentroOeste, TipoUnidade.CozinhaReduzida, "America/Sao_Paulo", new(7, 0), new(21, 0)), "asasul"),
            (new Unidade("Raízes Manaus Centro", "Manaus", "AM", Regiao.Norte, TipoUnidade.CozinhaReduzida, "America/Manaus", new(6, 0), new(21, 0)), "manaus")
        };
        db.Unidades.AddRange(unidades.Select(x => x.U));

        var junino = new JanelaSazonal(601, 731);
        var produtos = new List<Produto>
        {
            new("Tapioca de carne de sol com queijo coalho", "Tapioca na chapa com carne de sol desfiada e queijo coalho.", CategoriaProduto.Tapioca, 18.90m, false, null),
            new("Tapioca de coco com leite condensado", "Tapioca doce com coco ralado.", CategoriaProduto.Tapioca, 14.50m, false, null),
            new("Tapioca de frango com requeijão", null, CategoriaProduto.Tapioca, 16.90m, false, null),
            new("Cuscuz nordestino com ovo", "Cuscuz de milho no vapor com ovo e manteiga de garrafa.", CategoriaProduto.Cuscuz, 12.90m, true, null),
            new("Cuscuz recheado com charque", "Cuscuz recheado com charque acebolada.", CategoriaProduto.Cuscuz, 19.90m, true, null),
            new("Macaxeira frita com manteiga de garrafa", null, CategoriaProduto.Salgado, 13.90m, true, null),
            new("Coxinha de charque", null, CategoriaProduto.Salgado, 7.90m, false, null),
            new("Bolo de macaxeira (fatia)", null, CategoriaProduto.Bolo, 8.50m, false, null),
            new("Bolo de rolo (fatia)", "Bolo de rolo pernambucano com goiabada.", CategoriaProduto.Bolo, 9.90m, false, null),
            new("Suco de cajá 300ml", null, CategoriaProduto.Bebida, 8.90m, false, null),
            new("Suco de graviola 300ml", null, CategoriaProduto.Bebida, 8.90m, false, null),
            new("Café coado na hora", null, CategoriaProduto.Bebida, 5.50m, false, null),
            new("Café da manhã sertanejo", "Cuscuz, ovo, queijo coalho assado, macaxeira e café.", CategoriaProduto.CafeDaManha, 32.90m, true, null),
            new("Canjica", "Canjica cremosa de milho com canela (período junino).", CategoriaProduto.Doce, 9.90m, false, junino),
            new("Pamonha", "Pamonha de milho verde (período junino).", CategoriaProduto.Doce, 10.90m, true, junino),
            new("Milho cozido na manteiga", "Espiga de milho verde (período junino).", CategoriaProduto.Salgado, 7.50m, false, junino)
        };
        db.Produtos.AddRange(produtos);

        foreach (var (u, slug) in unidades)
        {
            foreach (var p in produtos.Where(p => p.CompativelCom(u.Tipo)))
            {
                var item = new ItemCardapio(u.Id, p.Id);
                decimal? precoLocal = u.Regiao == Regiao.Sudeste ? decimal.Round(p.PrecoBase * 1.15m, 1) : null;
                string? obs = u.Regiao == Regiao.Norte && p.Categoria == CategoriaProduto.Tapioca
                    ? "Variação regional: servida com tucumã a pedido." : null;
                item.Configurar(true, precoLocal, obs);
                db.ItensCardapio.Add(item);

                var estoque = new ItemEstoque(u.Id, p.Id, minimo: 20);
                estoque.Entrada(500);
                db.Estoques.Add(estoque);
                db.MovimentacoesEstoque.Add(new MovimentacaoEstoque(u.Id, p.Id, TipoMovimentacaoEstoque.Entrada, 500, "Carga inicial", null, null, agora));
            }

            db.Usuarios.AddRange(
                new Usuario($"Gerente {u.Nome}", $"gerente.{slug}@raizes.com.br", hash, Perfil.Gerente, u.Id, agora),
                new Usuario($"Atendente {u.Nome}", $"atendente.{slug}@raizes.com.br", hash, Perfil.Atendente, u.Id, agora),
                new Usuario($"Cozinha {u.Nome}", $"cozinha.{slug}@raizes.com.br", hash, Perfil.Cozinha, u.Id, agora),
                new Usuario($"Totem 01 {u.Nome}", $"totem.{slug}@raizes.com.br", hash, Perfil.Totem, u.Id, agora));
        }

        var clientes = new List<(Usuario U, Cliente C)>
        {
            Cliente("Maria das Graças", "maria@exemplo.com", new DateOnly(1985, 6, 24), true, true),
            Cliente("João Batista", "joao@exemplo.com", new DateOnly(2001, 3, 10), true, false),
            Cliente("Ana Clara", "ana@exemplo.com", null, false, false)
        };

        (Usuario, Cliente) Cliente(string nome, string email, DateOnly? nasc, bool fidelidade, bool campanhas)
        {
            var u = new Usuario(nome, email, hash, Perfil.Cliente, null, agora.AddDays(-120));
            var c = new Cliente(u.Id, nasc, null);
            db.Usuarios.Add(u);
            db.Clientes.Add(c);
            db.Consentimentos.AddRange(
                new Consentimento(c.Id, TipoConsentimento.TermosDeUso, true, "1.0", agora.AddDays(-120), "seed"),
                new Consentimento(c.Id, TipoConsentimento.ProgramaFidelidade, fidelidade, "1.0", agora.AddDays(-120), "seed"),
                new Consentimento(c.Id, TipoConsentimento.CampanhasPersonalizadas, campanhas, "1.0", agora.AddDays(-120), "seed"));
            return (u, c);
        }

        await db.SaveChangesAsync(ct);

        // Histórico de vendas dos últimos 60 dias para os relatórios da matriz (gerado com semente fixa: reproduzível).
        var rnd = new Random(2026);
        var pontosPorCliente = new Dictionary<Guid, int>();
        var cardapioPorUnidade = unidades.ToDictionary(x => x.U.Id, x => produtos.Where(p => p.CompativelCom(x.U.Tipo) && p.Sazonalidade is null).ToList());
        for (var i = 0; i < 400; i++)
        {
            var (u, _) = unidades[rnd.Next(unidades.Count)];
            var quando = agora.AddDays(-rnd.Next(1, 60)).AddMinutes(-rnd.Next(0, 600));
            var canal = (CanalPedido)rnd.Next(0, 5);
            // Pedidos de app/web/pickup sem cliente de demonstração usam João ou Ana; Maria recebe ~10 pedidos
            // recentes (nível Prata: entre 5 e 14 entregues em 90 dias).
            (Usuario U, Cliente C)? cliente = canal is CanalPedido.App or CanalPedido.Web or CanalPedido.Pickup ? clientes[1 + rnd.Next(2)] : null;
            if (i % 40 == 0) { canal = CanalPedido.App; cliente = clientes[0]; quando = agora.AddDays(-rnd.Next(1, 60)); }

            var disponiveis = cardapioPorUnidade[u.Id];
            var itens = Enumerable.Range(0, rnd.Next(1, 4))
                .Select(_ => disponiveis[rnd.Next(disponiveis.Count)]).DistinctBy(p => p.Id)
                .Select(p => new ItemNovoPedido(p.Id, p.Nome, u.Regiao == Regiao.Sudeste ? decimal.Round(p.PrecoBase * 1.15m, 1) : p.PrecoBase, rnd.Next(1, 3)))
                .ToList();
            var subtotal = itens.Sum(x => x.PrecoUnitario * x.Quantidade);
            var preco = CalculadoraPedido.Calcular(subtotal, 0, 0, 0, 0);
            var pedido = Pedido.Criar(u.Id, cliente?.C.Id, canal, cliente?.U.Id ?? Guid.Empty, $"seed-{i}", itens, preco, null, null, null, quando, TimeSpan.FromMinutes(15));

            var pagamento = new Pagamento(pedido.Id, pedido.Total, (MetodoPagamento)rnd.Next(0, 3), quando);
            if (i % 25 == 0)
            {
                pedido.Cancelar("Cliente desistiu (dado de demonstração)", Guid.Empty, true, quando.AddMinutes(2));
                pagamento.MarcarFalha("Não enviado", quando);
            }
            else
            {
                pagamento.RegistrarEnvio($"seed-tx-{i}", quando);
                pagamento.Aprovar(null, quando.AddSeconds(5));
                pedido.ConfirmarPagamento(quando.AddSeconds(5));
                pedido.IniciarPreparo(Guid.Empty, quando.AddMinutes(1));
                pedido.MarcarPronto(Guid.Empty, quando.AddMinutes(8));
                pedido.Entregar(Guid.Empty, quando.AddMinutes(10));
                if (cliente is { } cl && cl.C.Id != clientes[2].C.Id) // Ana não consentiu com o programa de fidelidade
                {
                    var pts = RegrasFidelidade.PontosGanhos(pedido.Total);
                    pontosPorCliente[cl.C.Id] = pontosPorCliente.GetValueOrDefault(cl.C.Id) + pts;
                    db.MovimentacoesPontos.Add(new MovimentacaoPontos(cl.C.Id, TipoMovimentacaoPontos.Credito, pts, pedido.Id, quando.AddMinutes(10)));
                }
            }
            pedido.LimparEventos(); // histórico de demonstração não gera eventos na Outbox
            db.Pedidos.Add(pedido);
            db.Pagamentos.Add(pagamento);
        }
        await db.SaveChangesAsync(ct);

        foreach (var (clienteId, pontos) in pontosPorCliente)
        {
            await db.Clientes.Where(c => c.Id == clienteId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.SaldoPontos, pontos), ct);
        }

        logger.LogInformation("Dados de demonstração carregados: {Unidades} unidades, {Produtos} produtos, 400 pedidos históricos.",
            unidades.Count, produtos.Count);
    }
}
