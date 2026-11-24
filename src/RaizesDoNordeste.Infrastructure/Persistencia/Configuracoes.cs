using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Campanhas;
using RaizesDoNordeste.Domain.Catalogo;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Estoques;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Pedidos;
using RaizesDoNordeste.Domain.Unidades;
using RaizesDoNordeste.Infrastructure.Outbox;

namespace RaizesDoNordeste.Infrastructure.Persistencia;

internal sealed class UsuarioConfig : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.ToTable("Usuarios");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Nome).HasMaxLength(120).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200).IsRequired();
        b.Property(x => x.SenhaHash).HasMaxLength(300).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
        b.HasOne<Unidade>().WithMany().HasForeignKey(x => x.UnidadeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ClienteConfig : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> b)
    {
        b.ToTable("Clientes", t => t.HasCheckConstraint("CK_Clientes_SaldoPontos", "[SaldoPontos] >= 0"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Telefone).HasMaxLength(20);
        b.HasOne<Usuario>().WithOne().HasForeignKey<Cliente>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConsentimentoConfig : IEntityTypeConfiguration<Consentimento>
{
    public void Configure(EntityTypeBuilder<Consentimento> b)
    {
        b.ToTable("Consentimentos");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.VersaoTermo).HasMaxLength(20).IsRequired();
        b.Property(x => x.Origem).HasMaxLength(60);
        b.HasIndex(x => new { x.ClienteId, x.Tipo, x.RegistradoEm });
        b.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MovimentacaoPontosConfig : IEntityTypeConfiguration<MovimentacaoPontos>
{
    public void Configure(EntityTypeBuilder<MovimentacaoPontos> b)
    {
        b.ToTable("MovimentacoesPontos");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Ignore(x => x.Efeito);
        b.HasIndex(x => new { x.ClienteId, x.OcorridoEm });
        b.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UnidadeConfig : IEntityTypeConfiguration<Unidade>
{
    public void Configure(EntityTypeBuilder<Unidade> b)
    {
        b.ToTable("Unidades");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Nome).HasMaxLength(120).IsRequired();
        b.Property(x => x.Cidade).HasMaxLength(80).IsRequired();
        b.Property(x => x.Uf).HasMaxLength(2).IsFixedLength().IsRequired();
        b.Property(x => x.FusoHorario).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Regiao);
    }
}

internal sealed class ProdutoConfig : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> b)
    {
        b.ToTable("Produtos");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Nome).HasMaxLength(120).IsRequired();
        b.Property(x => x.Descricao).HasMaxLength(500);
        b.Ignore(x => x.Sazonalidade);
    }
}

internal sealed class ItemCardapioConfig : IEntityTypeConfiguration<ItemCardapio>
{
    public void Configure(EntityTypeBuilder<ItemCardapio> b)
    {
        b.ToTable("ItensCardapio");
        b.HasKey(x => new { x.UnidadeId, x.ProdutoId });
        b.Property(x => x.ObservacaoRegional).HasMaxLength(300);
        b.HasOne(x => x.Produto).WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Unidade>().WithMany().HasForeignKey(x => x.UnidadeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ItemEstoqueConfig : IEntityTypeConfiguration<ItemEstoque>
{
    public void Configure(EntityTypeBuilder<ItemEstoque> b)
    {
        // A CHECK constraint é a última linha de defesa: mesmo com bug na aplicação, o banco recusa saldo negativo.
        b.ToTable("Estoques", t => t.HasCheckConstraint("CK_Estoques_Saldos",
            "[Quantidade] >= 0 AND [Reservada] >= 0 AND [Reservada] <= [Quantidade] AND [Minimo] >= 0"));
        b.HasKey(x => new { x.UnidadeId, x.ProdutoId });
        b.Property(x => x.Versao).IsRowVersion();
        b.Ignore(x => x.Disponivel);
        b.Ignore(x => x.AbaixoDoMinimo);
        b.HasOne<Unidade>().WithMany().HasForeignKey(x => x.UnidadeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MovimentacaoEstoqueConfig : IEntityTypeConfiguration<MovimentacaoEstoque>
{
    public void Configure(EntityTypeBuilder<MovimentacaoEstoque> b)
    {
        b.ToTable("MovimentacoesEstoque");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Motivo).HasMaxLength(300);
        b.HasIndex(x => new { x.UnidadeId, x.ProdutoId, x.OcorridoEm });
        b.HasIndex(x => x.PedidoId);
    }
}

internal sealed class PedidoConfig : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> b)
    {
        b.ToTable("Pedidos", t => t.HasCheckConstraint("CK_Pedidos_Total", "[Total] >= 0 AND [Subtotal] > 0"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ChaveIdempotencia).HasMaxLength(100).IsRequired();
        b.Property(x => x.NomeChamada).HasMaxLength(40);
        b.Property(x => x.Observacao).HasMaxLength(200);
        b.Property(x => x.MotivoCancelamento).HasMaxLength(300);
        b.Property(x => x.Versao).IsRowVersion();
        b.Ignore(x => x.Eventos);
        b.Ignore(x => x.DescontoTotal);
        b.Ignore(x => x.EstaFinalizado);

        b.HasIndex(x => new { x.CriadoPorUsuarioId, x.ChaveIdempotencia }).IsUnique();
        b.HasIndex(x => new { x.UnidadeId, x.Status, x.CriadoEm });
        b.HasIndex(x => new { x.ClienteId, x.Status, x.CriadoEm });
        b.HasIndex(x => new { x.Status, x.ExpiraEm });
        b.HasIndex(x => x.CriadoEm);
        b.HasIndex(x => new { x.CanalPedido, x.CriadoEm });

        b.HasOne<Unidade>().WithMany().HasForeignKey(x => x.UnidadeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Campanha>().WithMany().HasForeignKey(x => x.CampanhaId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Historico).WithOne().HasForeignKey(h => h.PedidoId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Historico).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PedidoItemConfig : IEntityTypeConfiguration<PedidoItem>
{
    public void Configure(EntityTypeBuilder<PedidoItem> b)
    {
        b.ToTable("PedidoItens", t => t.HasCheckConstraint("CK_PedidoItens_Quantidade", "[Quantidade] > 0"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.NomeProduto).HasMaxLength(120).IsRequired();
        b.Ignore(x => x.Subtotal);
        b.HasIndex(x => x.ProdutoId);
        b.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HistoricoStatusPedidoConfig : IEntityTypeConfiguration<HistoricoStatusPedido>
{
    public void Configure(EntityTypeBuilder<HistoricoStatusPedido> b)
    {
        b.ToTable("PedidoHistoricoStatus");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Motivo).HasMaxLength(300);
    }
}

internal sealed class PagamentoConfig : IEntityTypeConfiguration<Pagamento>
{
    public void Configure(EntityTypeBuilder<Pagamento> b)
    {
        b.ToTable("Pagamentos");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TransacaoExternaId).HasMaxLength(100);
        b.Property(x => x.Detalhe).HasMaxLength(300);
        b.Property(x => x.Versao).IsRowVersion();
        b.Ignore(x => x.EstaFinalizado);
        b.HasIndex(x => x.TransacaoExternaId);
        b.HasIndex(x => new { x.Status, x.CriadoEm });
        // No máximo um pagamento "ativo" por pedido: impede cobrança dupla mesmo com requisições simultâneas.
        b.HasIndex(x => x.PedidoId).IsUnique().HasFilter("[Status] IN ('Pendente', 'Aprovado')").HasDatabaseName("IX_Pagamentos_PedidoId_Ativo");
        b.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CampanhaConfig : IEntityTypeConfiguration<Campanha>
{
    public void Configure(EntityTypeBuilder<Campanha> b)
    {
        b.ToTable("Campanhas");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Nome).HasMaxLength(120).IsRequired();
        b.Property(x => x.Descricao).HasMaxLength(500);
        b.Property(x => x.PercentualDesconto).HasPrecision(5, 4);
        b.Ignore(x => x.Segmentacao);
        b.HasIndex(x => new { x.Ativa, x.InicioEm, x.FimEm });
    }
}

internal sealed class RegistroAuditoriaConfig : IEntityTypeConfiguration<RegistroAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistroAuditoria> b)
    {
        b.ToTable("Auditoria");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Acao).HasMaxLength(60).IsRequired();
        b.Property(x => x.Entidade).HasMaxLength(60).IsRequired();
        b.Property(x => x.EntidadeId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Ip).HasMaxLength(45);
        b.Property(x => x.CorrelacaoId).HasMaxLength(64);
        b.HasIndex(x => x.OcorridoEm);
        b.HasIndex(x => new { x.Acao, x.OcorridoEm });
        b.HasIndex(x => new { x.Entidade, x.EntidadeId });
        b.HasIndex(x => new { x.UnidadeId, x.OcorridoEm });
    }
}

internal sealed class OutboxMessageConfig : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("Outbox");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Tipo).HasMaxLength(200).IsRequired();
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.UltimoErro).HasMaxLength(1000);
        b.HasIndex(x => x.OcorridoEm).HasFilter("[ProcessadoEm] IS NULL").HasDatabaseName("IX_Outbox_Pendentes");
    }
}
