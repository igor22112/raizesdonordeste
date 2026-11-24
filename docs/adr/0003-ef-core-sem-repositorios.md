# ADR-0003 — EF Core direto nos casos de uso (sem repositório genérico)

**Status:** aceita

## Contexto
É comum envolver o EF Core em repositórios genéricos. O EF já implementa *Unit of Work* (`DbContext`) e
*Repository* (`DbSet`), e os relatórios da matriz precisam de consultas agregadas (GROUP BY, JOIN) eficientes.

## Decisão
A camada Application depende de `IAppDbContext` (interface que expõe os `DbSet` e `EmTransacaoAsync`). As regras
de negócio ficam nas entidades do domínio (que não conhecem o EF). A Infrastructure implementa a interface.

## Alternativas
- Repositório por agregado: bom isolamento, mais código e consultas de relatório mais difíceis.

## Consequências
- Menos código e consultas LINQ traduzidas para SQL.
- A Application conhece o EF Core (pacote). Troca de ORM exigiria ajustes nos serviços — risco aceito.
- Casos de uso testados por testes de integração com SQL Server real (Testcontainers), não com mocks de DbSet.
