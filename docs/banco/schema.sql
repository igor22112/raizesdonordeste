IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [Auditoria] (
    [Id] uniqueidentifier NOT NULL,
    [OcorridoEm] datetimeoffset NOT NULL,
    [UsuarioId] uniqueidentifier NULL,
    [Perfil] nvarchar(30) NULL,
    [UnidadeId] uniqueidentifier NULL,
    [Acao] nvarchar(60) NOT NULL,
    [Entidade] nvarchar(60) NOT NULL,
    [EntidadeId] nvarchar(100) NOT NULL,
    [DetalhesJson] nvarchar(max) NULL,
    [Ip] nvarchar(45) NULL,
    [CorrelacaoId] nvarchar(64) NULL,
    CONSTRAINT [PK_Auditoria] PRIMARY KEY ([Id])
);

CREATE TABLE [Campanhas] (
    [Id] uniqueidentifier NOT NULL,
    [Nome] nvarchar(120) NOT NULL,
    [Descricao] nvarchar(500) NULL,
    [PercentualDesconto] decimal(5,4) NOT NULL,
    [InicioEm] datetimeoffset NOT NULL,
    [FimEm] datetimeoffset NOT NULL,
    [Ativa] bit NOT NULL,
    [FrequenciaMinima] int NULL,
    [NivelMinimo] nvarchar(30) NULL,
    [Regiao] nvarchar(30) NULL,
    CONSTRAINT [PK_Campanhas] PRIMARY KEY ([Id])
);

CREATE TABLE [MovimentacoesEstoque] (
    [Id] uniqueidentifier NOT NULL,
    [UnidadeId] uniqueidentifier NOT NULL,
    [ProdutoId] uniqueidentifier NOT NULL,
    [Tipo] nvarchar(30) NOT NULL,
    [Quantidade] int NOT NULL,
    [Motivo] nvarchar(300) NULL,
    [UsuarioId] uniqueidentifier NULL,
    [PedidoId] uniqueidentifier NULL,
    [OcorridoEm] datetimeoffset NOT NULL,
    CONSTRAINT [PK_MovimentacoesEstoque] PRIMARY KEY ([Id])
);

CREATE TABLE [Outbox] (
    [Id] uniqueidentifier NOT NULL,
    [Tipo] nvarchar(200) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [OcorridoEm] datetimeoffset NOT NULL,
    [ProcessadoEm] datetimeoffset NULL,
    [Tentativas] int NOT NULL,
    [UltimoErro] nvarchar(1000) NULL,
    CONSTRAINT [PK_Outbox] PRIMARY KEY ([Id])
);

CREATE TABLE [Produtos] (
    [Id] uniqueidentifier NOT NULL,
    [Nome] nvarchar(120) NOT NULL,
    [Descricao] nvarchar(500) NULL,
    [Categoria] nvarchar(30) NOT NULL,
    [PrecoBase] decimal(10,2) NOT NULL,
    [RequerCozinhaCompleta] bit NOT NULL,
    [SazonalInicioMmdd] int NULL,
    [SazonalFimMmdd] int NULL,
    [Ativo] bit NOT NULL,
    CONSTRAINT [PK_Produtos] PRIMARY KEY ([Id])
);

CREATE TABLE [Unidades] (
    [Id] uniqueidentifier NOT NULL,
    [Nome] nvarchar(120) NOT NULL,
    [Cidade] nvarchar(80) NOT NULL,
    [Uf] nchar(2) NOT NULL,
    [Regiao] nvarchar(30) NOT NULL,
    [Tipo] nvarchar(30) NOT NULL,
    [FusoHorario] nvarchar(60) NOT NULL,
    [HoraAbertura] time NOT NULL,
    [HoraFechamento] time NOT NULL,
    [Ativa] bit NOT NULL,
    CONSTRAINT [PK_Unidades] PRIMARY KEY ([Id])
);

CREATE TABLE [Estoques] (
    [UnidadeId] uniqueidentifier NOT NULL,
    [ProdutoId] uniqueidentifier NOT NULL,
    [Quantidade] int NOT NULL,
    [Reservada] int NOT NULL,
    [Minimo] int NOT NULL,
    [Versao] rowversion NOT NULL,
    CONSTRAINT [PK_Estoques] PRIMARY KEY ([UnidadeId], [ProdutoId]),
    CONSTRAINT [CK_Estoques_Saldos] CHECK ([Quantidade] >= 0 AND [Reservada] >= 0 AND [Reservada] <= [Quantidade] AND [Minimo] >= 0),
    CONSTRAINT [FK_Estoques_Produtos_ProdutoId] FOREIGN KEY ([ProdutoId]) REFERENCES [Produtos] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Estoques_Unidades_UnidadeId] FOREIGN KEY ([UnidadeId]) REFERENCES [Unidades] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ItensCardapio] (
    [UnidadeId] uniqueidentifier NOT NULL,
    [ProdutoId] uniqueidentifier NOT NULL,
    [PrecoLocal] decimal(10,2) NULL,
    [Disponivel] bit NOT NULL,
    [ObservacaoRegional] nvarchar(300) NULL,
    CONSTRAINT [PK_ItensCardapio] PRIMARY KEY ([UnidadeId], [ProdutoId]),
    CONSTRAINT [FK_ItensCardapio_Produtos_ProdutoId] FOREIGN KEY ([ProdutoId]) REFERENCES [Produtos] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ItensCardapio_Unidades_UnidadeId] FOREIGN KEY ([UnidadeId]) REFERENCES [Unidades] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Usuarios] (
    [Id] uniqueidentifier NOT NULL,
    [Nome] nvarchar(120) NOT NULL,
    [Email] nvarchar(200) NOT NULL,
    [SenhaHash] nvarchar(300) NOT NULL,
    [Perfil] nvarchar(30) NOT NULL,
    [UnidadeId] uniqueidentifier NULL,
    [Ativo] bit NOT NULL,
    [CriadoEm] datetimeoffset NOT NULL,
    CONSTRAINT [PK_Usuarios] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Usuarios_Unidades_UnidadeId] FOREIGN KEY ([UnidadeId]) REFERENCES [Unidades] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Clientes] (
    [Id] uniqueidentifier NOT NULL,
    [DataNascimento] date NULL,
    [Telefone] nvarchar(20) NULL,
    [SaldoPontos] int NOT NULL,
    [Anonimizado] bit NOT NULL,
    [AnonimizadoEm] datetimeoffset NULL,
    CONSTRAINT [PK_Clientes] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Clientes_SaldoPontos] CHECK ([SaldoPontos] >= 0),
    CONSTRAINT [FK_Clientes_Usuarios_Id] FOREIGN KEY ([Id]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Consentimentos] (
    [Id] uniqueidentifier NOT NULL,
    [ClienteId] uniqueidentifier NOT NULL,
    [Tipo] nvarchar(30) NOT NULL,
    [Concedido] bit NOT NULL,
    [VersaoTermo] nvarchar(20) NOT NULL,
    [RegistradoEm] datetimeoffset NOT NULL,
    [Origem] nvarchar(60) NULL,
    CONSTRAINT [PK_Consentimentos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Consentimentos_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [MovimentacoesPontos] (
    [Id] uniqueidentifier NOT NULL,
    [ClienteId] uniqueidentifier NOT NULL,
    [Tipo] nvarchar(30) NOT NULL,
    [Pontos] int NOT NULL,
    [PedidoId] uniqueidentifier NULL,
    [OcorridoEm] datetimeoffset NOT NULL,
    CONSTRAINT [PK_MovimentacoesPontos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MovimentacoesPontos_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Pedidos] (
    [Id] uniqueidentifier NOT NULL,
    [UnidadeId] uniqueidentifier NOT NULL,
    [ClienteId] uniqueidentifier NULL,
    [CriadoPorUsuarioId] uniqueidentifier NOT NULL,
    [ChaveIdempotencia] nvarchar(100) NOT NULL,
    [CanalPedido] nvarchar(30) NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [NomeChamada] nvarchar(40) NULL,
    [Observacao] nvarchar(200) NULL,
    [Subtotal] decimal(10,2) NOT NULL,
    [DescontoNivel] decimal(10,2) NOT NULL,
    [DescontoCampanha] decimal(10,2) NOT NULL,
    [DescontoPontos] decimal(10,2) NOT NULL,
    [Total] decimal(10,2) NOT NULL,
    [PontosResgatados] int NOT NULL,
    [CampanhaId] uniqueidentifier NULL,
    [MotivoCancelamento] nvarchar(300) NULL,
    [CriadoEm] datetimeoffset NOT NULL,
    [AtualizadoEm] datetimeoffset NOT NULL,
    [ExpiraEm] datetimeoffset NOT NULL,
    [Versao] rowversion NOT NULL,
    CONSTRAINT [PK_Pedidos] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Pedidos_Total] CHECK ([Total] >= 0 AND [Subtotal] > 0),
    CONSTRAINT [FK_Pedidos_Campanhas_CampanhaId] FOREIGN KEY ([CampanhaId]) REFERENCES [Campanhas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Pedidos_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Pedidos_Unidades_UnidadeId] FOREIGN KEY ([UnidadeId]) REFERENCES [Unidades] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Pagamentos] (
    [Id] uniqueidentifier NOT NULL,
    [PedidoId] uniqueidentifier NOT NULL,
    [Valor] decimal(10,2) NOT NULL,
    [Metodo] nvarchar(30) NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [TransacaoExternaId] nvarchar(100) NULL,
    [Detalhe] nvarchar(300) NULL,
    [CriadoEm] datetimeoffset NOT NULL,
    [AtualizadoEm] datetimeoffset NOT NULL,
    [Versao] rowversion NOT NULL,
    CONSTRAINT [PK_Pagamentos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Pagamentos_Pedidos_PedidoId] FOREIGN KEY ([PedidoId]) REFERENCES [Pedidos] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [PedidoHistoricoStatus] (
    [Id] uniqueidentifier NOT NULL,
    [PedidoId] uniqueidentifier NOT NULL,
    [De] nvarchar(30) NULL,
    [Para] nvarchar(30) NOT NULL,
    [UsuarioId] uniqueidentifier NULL,
    [Motivo] nvarchar(300) NULL,
    [OcorridoEm] datetimeoffset NOT NULL,
    CONSTRAINT [PK_PedidoHistoricoStatus] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PedidoHistoricoStatus_Pedidos_PedidoId] FOREIGN KEY ([PedidoId]) REFERENCES [Pedidos] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [PedidoItens] (
    [Id] uniqueidentifier NOT NULL,
    [PedidoId] uniqueidentifier NOT NULL,
    [ProdutoId] uniqueidentifier NOT NULL,
    [NomeProduto] nvarchar(120) NOT NULL,
    [PrecoUnitario] decimal(10,2) NOT NULL,
    [Quantidade] int NOT NULL,
    CONSTRAINT [PK_PedidoItens] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PedidoItens_Quantidade] CHECK ([Quantidade] > 0),
    CONSTRAINT [FK_PedidoItens_Pedidos_PedidoId] FOREIGN KEY ([PedidoId]) REFERENCES [Pedidos] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PedidoItens_Produtos_ProdutoId] FOREIGN KEY ([ProdutoId]) REFERENCES [Produtos] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_Auditoria_Acao_OcorridoEm] ON [Auditoria] ([Acao], [OcorridoEm]);

CREATE INDEX [IX_Auditoria_Entidade_EntidadeId] ON [Auditoria] ([Entidade], [EntidadeId]);

CREATE INDEX [IX_Auditoria_OcorridoEm] ON [Auditoria] ([OcorridoEm]);

CREATE INDEX [IX_Auditoria_UnidadeId_OcorridoEm] ON [Auditoria] ([UnidadeId], [OcorridoEm]);

CREATE INDEX [IX_Campanhas_Ativa_InicioEm_FimEm] ON [Campanhas] ([Ativa], [InicioEm], [FimEm]);

CREATE INDEX [IX_Consentimentos_ClienteId_Tipo_RegistradoEm] ON [Consentimentos] ([ClienteId], [Tipo], [RegistradoEm]);

CREATE INDEX [IX_Estoques_ProdutoId] ON [Estoques] ([ProdutoId]);

CREATE INDEX [IX_ItensCardapio_ProdutoId] ON [ItensCardapio] ([ProdutoId]);

CREATE INDEX [IX_MovimentacoesEstoque_PedidoId] ON [MovimentacoesEstoque] ([PedidoId]);

CREATE INDEX [IX_MovimentacoesEstoque_UnidadeId_ProdutoId_OcorridoEm] ON [MovimentacoesEstoque] ([UnidadeId], [ProdutoId], [OcorridoEm]);

CREATE INDEX [IX_MovimentacoesPontos_ClienteId_OcorridoEm] ON [MovimentacoesPontos] ([ClienteId], [OcorridoEm]);

CREATE INDEX [IX_Outbox_Pendentes] ON [Outbox] ([OcorridoEm]) WHERE [ProcessadoEm] IS NULL;

CREATE UNIQUE INDEX [IX_Pagamentos_PedidoId_Ativo] ON [Pagamentos] ([PedidoId]) WHERE [Status] IN ('Pendente', 'Aprovado');

CREATE INDEX [IX_Pagamentos_Status_CriadoEm] ON [Pagamentos] ([Status], [CriadoEm]);

CREATE INDEX [IX_Pagamentos_TransacaoExternaId] ON [Pagamentos] ([TransacaoExternaId]);

CREATE INDEX [IX_PedidoHistoricoStatus_PedidoId] ON [PedidoHistoricoStatus] ([PedidoId]);

CREATE INDEX [IX_PedidoItens_PedidoId] ON [PedidoItens] ([PedidoId]);

CREATE INDEX [IX_PedidoItens_ProdutoId] ON [PedidoItens] ([ProdutoId]);

CREATE INDEX [IX_Pedidos_CampanhaId] ON [Pedidos] ([CampanhaId]);

CREATE INDEX [IX_Pedidos_CanalPedido_CriadoEm] ON [Pedidos] ([CanalPedido], [CriadoEm]);

CREATE INDEX [IX_Pedidos_ClienteId_Status_CriadoEm] ON [Pedidos] ([ClienteId], [Status], [CriadoEm]);

CREATE INDEX [IX_Pedidos_CriadoEm] ON [Pedidos] ([CriadoEm]);

CREATE UNIQUE INDEX [IX_Pedidos_CriadoPorUsuarioId_ChaveIdempotencia] ON [Pedidos] ([CriadoPorUsuarioId], [ChaveIdempotencia]);

CREATE INDEX [IX_Pedidos_Status_ExpiraEm] ON [Pedidos] ([Status], [ExpiraEm]);

CREATE INDEX [IX_Pedidos_UnidadeId_Status_CriadoEm] ON [Pedidos] ([UnidadeId], [Status], [CriadoEm]);

CREATE INDEX [IX_Unidades_Regiao] ON [Unidades] ([Regiao]);

CREATE UNIQUE INDEX [IX_Usuarios_Email] ON [Usuarios] ([Email]);

CREATE INDEX [IX_Usuarios_UnidadeId] ON [Usuarios] ([UnidadeId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113220046_Inicial', N'10.0.15');

COMMIT;
GO

