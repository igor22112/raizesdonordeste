using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Campanhas;
using RaizesDoNordeste.Domain.Catalogo;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Estoques;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.Domain.Unidades;

namespace RaizesDoNordeste.Application.Abstracoes;

/// <summary>
/// Porta de persistência usada pelos casos de uso. Optamos por expor DbSet (EF Core) em vez de
/// repositórios genéricos: o EF já implementa Unit of Work e Repository, e LINQ permite consultas
/// de relatório eficientes sem uma camada extra (ver ADR-0003).
/// </summary>
public interface IAppDbContext
{
    DbSet<Usuario> Usuarios { get; }
    DbSet<Cliente> Clientes { get; }
    DbSet<Consentimento> Consentimentos { get; }
    DbSet<MovimentacaoPontos> MovimentacoesPontos { get; }
    DbSet<Unidade> Unidades { get; }
    DbSet<Produto> Produtos { get; }
    DbSet<ItemCardapio> ItensCardapio { get; }
    DbSet<ItemEstoque> Estoques { get; }
    DbSet<MovimentacaoEstoque> MovimentacoesEstoque { get; }
    DbSet<Pedido> Pedidos { get; }
    DbSet<Pagamento> Pagamentos { get; }
    DbSet<Campanha> Campanhas { get; }
    DbSet<RegistroAuditoria> Auditoria { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Executa a operação em uma transação, com a estratégia de re-tentativa do provedor
    /// (falhas transitórias do SQL Server). A operação deve carregar os dados de que precisa
    /// dentro do delegate, pois pode ser executada mais de uma vez.
    /// </summary>
    Task<T> EmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct = default);

    void LimparRastreamento();
}
