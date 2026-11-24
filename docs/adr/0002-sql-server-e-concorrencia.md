# ADR-0002 — SQL Server, concorrência otimista e UPDATE atômico no estoque

**Status:** aceita

## Contexto
Dados transacionais e relacionais (pedidos, itens, estoque, pagamentos, auditoria) com necessidade de
integridade forte. No pico, muitos clientes compram o mesmo item ao mesmo tempo na mesma unidade.

## Decisão
- **SQL Server 2022** como banco relacional (ACID, maduro no ecossistema .NET, réplicas de leitura e
  Always On para alta disponibilidade em produção).
- **Reserva de estoque com UPDATE atômico condicional**:
  `UPDATE Estoques SET Reservada = Reservada + @q WHERE ... AND Quantidade - Reservada >= @q`.
  Se nenhuma linha for afetada, não há saldo e o pedido é recusado (409). Itens são processados em ordem de
  `ProdutoId` para evitar deadlocks.
- **Concorrência otimista (`rowversion`)** em `Pedidos`, `Pagamentos` e `Estoques` para operações que leem e
  depois gravam (mudanças de status, ajustes manuais). Conflito → HTTP 409 ou re-tentativa interna.
- **CHECK constraints** como última barreira (`Quantidade >= 0`, `Reservada <= Quantidade`, `SaldoPontos >= 0`).
- **Índice único filtrado** `IX_Pagamentos_PedidoId_Ativo` (no máximo um pagamento Pendente/Aprovado por pedido).
- Enums gravados como texto (legibilidade em consultas e relatórios ad hoc).

## Alternativas
- Lock pessimista (`SELECT ... WITH (UPDLOCK)`): correto, mas segura locks por mais tempo.
- Banco NoSQL: flexível, mas sem transações multi-entidade simples para pedido + estoque + pontos.

## Consequências
- Teste CT19: 12 pedidos simultâneos para 5 unidades em estoque → exatamente 5 aceitos.
- Estoque modelado por **produto vendável** (porções). Ficha técnica de insumos fica como evolução.
