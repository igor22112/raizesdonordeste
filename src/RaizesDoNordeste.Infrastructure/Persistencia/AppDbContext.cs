using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Campanhas;
using RaizesDoNordeste.Domain.Catalogo;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Estoques;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.Domain.Unidades;
using RaizesDoNordeste.Infrastructure.Outbox;

namespace RaizesDoNordeste.Infrastructure.Persistencia;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider relogio) : DbContext(options), IAppDbContext
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Consentimento> Consentimentos => Set<Consentimento>();
    public DbSet<MovimentacaoPontos> MovimentacoesPontos => Set<MovimentacaoPontos>();
    public DbSet<Unidade> Unidades => Set<Unidade>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ItemCardapio> ItensCardapio => Set<ItemCardapio>();
    public DbSet<ItemEstoque> Estoques => Set<ItemEstoque>();
    public DbSet<MovimentacaoEstoque> MovimentacoesEstoque => Set<MovimentacaoEstoque>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<Campanha> Campanhas => Set<Campanha>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Enums como texto: o banco fica legível em consultas e relatórios ad hoc e a ordem do enum pode mudar sem migração de dados.
        b.Properties<Enum>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<decimal>().HavePrecision(10, 2);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<RegistroAuditoria>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Registros de auditoria são imutáveis (append-only).");
        }

        // Outbox: eventos de domínio são gravados na MESMA transação das mudanças de estado.
        // Assim nunca existe pedido alterado sem evento, nem evento de algo que não foi salvo.
        var agora = relogio.GetUtcNow();
        var agregados = ChangeTracker.Entries<RaizAgregado>().Select(e => e.Entity).Where(a => a.Eventos.Count > 0).ToList();
        foreach (var agregado in agregados)
        {
            foreach (var evento in agregado.Eventos)
                Outbox.Add(new OutboxMessage(evento.GetType().Name, JsonSerializer.Serialize(evento, evento.GetType(), JsonOpcoes.Padrao), agora));
            agregado.LimparEventos();
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    public async Task<T> EmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct = default)
    {
        if (Database.CurrentTransaction is not null) return await operacao(ct);

        var estrategia = Database.CreateExecutionStrategy();
        var tentativa = 0;
        return await estrategia.ExecuteAsync(async () =>
        {
            if (tentativa++ > 0) ChangeTracker.Clear(); // re-tentativa após falha transitória: recomeça do zero
            await using var transacao = await Database.BeginTransactionAsync(ct);
            var resultado = await operacao(ct);
            await transacao.CommitAsync(ct);
            return resultado;
        });
    }

    public void LimparRastreamento() => ChangeTracker.Clear();
}

internal static class JsonOpcoes
{
    public static readonly JsonSerializerOptions Padrao = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
