using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaizesDoNordeste.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Auditoria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Perfil = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    UnidadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Acao = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Entidade = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntidadeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DetalhesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CorrelacaoId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auditoria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campanhas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PercentualDesconto = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    InicioEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FimEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    FrequenciaMinima = table.Column<int>(type: "int", nullable: true),
                    NivelMinimo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Regiao = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campanhas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MovimentacoesEstoque",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnidadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentacoesEstoque", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    UltimoErro = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Produtos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Categoria = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PrecoBase = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    RequerCozinhaCompleta = table.Column<bool>(type: "bit", nullable: false),
                    SazonalInicioMmdd = table.Column<int>(type: "int", nullable: true),
                    SazonalFimMmdd = table.Column<int>(type: "int", nullable: true),
                    Ativo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produtos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Unidades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Cidade = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Uf = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Regiao = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FusoHorario = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    HoraAbertura = table.Column<TimeOnly>(type: "time", nullable: false),
                    HoraFechamento = table.Column<TimeOnly>(type: "time", nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Unidades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Estoques",
                columns: table => new
                {
                    UnidadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    Reservada = table.Column<int>(type: "int", nullable: false),
                    Minimo = table.Column<int>(type: "int", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Estoques", x => new { x.UnidadeId, x.ProdutoId });
                    table.CheckConstraint("CK_Estoques_Saldos", "[Quantidade] >= 0 AND [Reservada] >= 0 AND [Reservada] <= [Quantidade] AND [Minimo] >= 0");
                    table.ForeignKey(
                        name: "FK_Estoques_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Estoques_Unidades_UnidadeId",
                        column: x => x.UnidadeId,
                        principalTable: "Unidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItensCardapio",
                columns: table => new
                {
                    UnidadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrecoLocal = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    Disponivel = table.Column<bool>(type: "bit", nullable: false),
                    ObservacaoRegional = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensCardapio", x => new { x.UnidadeId, x.ProdutoId });
                    table.ForeignKey(
                        name: "FK_ItensCardapio_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensCardapio_Unidades_UnidadeId",
                        column: x => x.UnidadeId,
                        principalTable: "Unidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SenhaHash = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Perfil = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UnidadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Ativo = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Usuarios_Unidades_UnidadeId",
                        column: x => x.UnidadeId,
                        principalTable: "Unidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataNascimento = table.Column<DateOnly>(type: "date", nullable: true),
                    Telefone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SaldoPontos = table.Column<int>(type: "int", nullable: false),
                    Anonimizado = table.Column<bool>(type: "bit", nullable: false),
                    AnonimizadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                    table.CheckConstraint("CK_Clientes_SaldoPontos", "[SaldoPontos] >= 0");
                    table.ForeignKey(
                        name: "FK_Clientes_Usuarios_Id",
                        column: x => x.Id,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Consentimentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Concedido = table.Column<bool>(type: "bit", nullable: false),
                    VersaoTermo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegistradoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Origem = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consentimentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Consentimentos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentacoesPontos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Pontos = table.Column<int>(type: "int", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentacoesPontos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentacoesPontos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pedidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnidadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CriadoPorUsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CanalPedido = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NomeChamada = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Observacao = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DescontoNivel = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DescontoCampanha = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DescontoPontos = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PontosResgatados = table.Column<int>(type: "int", nullable: false),
                    CampanhaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pedidos", x => x.Id);
                    table.CheckConstraint("CK_Pedidos_Total", "[Total] >= 0 AND [Subtotal] > 0");
                    table.ForeignKey(
                        name: "FK_Pedidos_Campanhas_CampanhaId",
                        column: x => x.CampanhaId,
                        principalTable: "Campanhas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pedidos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pedidos_Unidades_UnidadeId",
                        column: x => x.UnidadeId,
                        principalTable: "Unidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pagamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Metodo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TransacaoExternaId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Detalhe = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pagamentos_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PedidoHistoricoStatus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    De = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Para = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidoHistoricoStatus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PedidoHistoricoStatus_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PedidoItens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeProduto = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidoItens", x => x.Id);
                    table.CheckConstraint("CK_PedidoItens_Quantidade", "[Quantidade] > 0");
                    table.ForeignKey(
                        name: "FK_PedidoItens_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PedidoItens_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_Acao_OcorridoEm",
                table: "Auditoria",
                columns: new[] { "Acao", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_Entidade_EntidadeId",
                table: "Auditoria",
                columns: new[] { "Entidade", "EntidadeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_OcorridoEm",
                table: "Auditoria",
                column: "OcorridoEm");

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_UnidadeId_OcorridoEm",
                table: "Auditoria",
                columns: new[] { "UnidadeId", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Campanhas_Ativa_InicioEm_FimEm",
                table: "Campanhas",
                columns: new[] { "Ativa", "InicioEm", "FimEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Consentimentos_ClienteId_Tipo_RegistradoEm",
                table: "Consentimentos",
                columns: new[] { "ClienteId", "Tipo", "RegistradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Estoques_ProdutoId",
                table: "Estoques",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensCardapio_ProdutoId",
                table: "ItensCardapio",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentacoesEstoque_PedidoId",
                table: "MovimentacoesEstoque",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentacoesEstoque_UnidadeId_ProdutoId_OcorridoEm",
                table: "MovimentacoesEstoque",
                columns: new[] { "UnidadeId", "ProdutoId", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentacoesPontos_ClienteId_OcorridoEm",
                table: "MovimentacoesPontos",
                columns: new[] { "ClienteId", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Pendentes",
                table: "Outbox",
                column: "OcorridoEm",
                filter: "[ProcessadoEm] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_PedidoId_Ativo",
                table: "Pagamentos",
                column: "PedidoId",
                unique: true,
                filter: "[Status] IN ('Pendente', 'Aprovado')");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_Status_CriadoEm",
                table: "Pagamentos",
                columns: new[] { "Status", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_TransacaoExternaId",
                table: "Pagamentos",
                column: "TransacaoExternaId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoHistoricoStatus_PedidoId",
                table: "PedidoHistoricoStatus",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_PedidoId",
                table: "PedidoItens",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_ProdutoId",
                table: "PedidoItens",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CampanhaId",
                table: "Pedidos",
                column: "CampanhaId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CanalPedido_CriadoEm",
                table: "Pedidos",
                columns: new[] { "CanalPedido", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_ClienteId_Status_CriadoEm",
                table: "Pedidos",
                columns: new[] { "ClienteId", "Status", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CriadoEm",
                table: "Pedidos",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CriadoPorUsuarioId_ChaveIdempotencia",
                table: "Pedidos",
                columns: new[] { "CriadoPorUsuarioId", "ChaveIdempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Status_ExpiraEm",
                table: "Pedidos",
                columns: new[] { "Status", "ExpiraEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_UnidadeId_Status_CriadoEm",
                table: "Pedidos",
                columns: new[] { "UnidadeId", "Status", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Unidades_Regiao",
                table: "Unidades",
                column: "Regiao");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_UnidadeId",
                table: "Usuarios",
                column: "UnidadeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Auditoria");

            migrationBuilder.DropTable(
                name: "Consentimentos");

            migrationBuilder.DropTable(
                name: "Estoques");

            migrationBuilder.DropTable(
                name: "ItensCardapio");

            migrationBuilder.DropTable(
                name: "MovimentacoesEstoque");

            migrationBuilder.DropTable(
                name: "MovimentacoesPontos");

            migrationBuilder.DropTable(
                name: "Outbox");

            migrationBuilder.DropTable(
                name: "Pagamentos");

            migrationBuilder.DropTable(
                name: "PedidoHistoricoStatus");

            migrationBuilder.DropTable(
                name: "PedidoItens");

            migrationBuilder.DropTable(
                name: "Pedidos");

            migrationBuilder.DropTable(
                name: "Produtos");

            migrationBuilder.DropTable(
                name: "Campanhas");

            migrationBuilder.DropTable(
                name: "Clientes");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "Unidades");
        }
    }
}
