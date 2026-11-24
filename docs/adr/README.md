# Registros de Decisão de Arquitetura (ADR)

Formato curto: contexto → decisão → alternativas → consequências.

| ADR | Título |
|---|---|
| [0001](0001-monolito-modular.md) | Monólito modular em camadas, com pagamento como serviço externo |
| [0002](0002-sql-server-e-concorrencia.md) | SQL Server, concorrência otimista e UPDATE atômico no estoque |
| [0003](0003-ef-core-sem-repositorios.md) | EF Core direto nos casos de uso (sem repositório genérico) |
| [0004](0004-pagamento-assincrono-resiliente.md) | Pagamento assíncrono: idempotência, resiliência, webhook assinado e outbox |
| [0005](0005-regras-fidelidade.md) | Regras de fidelidade e descontos |
| [0006](0006-lgpd.md) | LGPD: consentimento versionado, minimização e anonimização |
| [0007](0007-seguranca-api.md) | Autenticação JWT, autorização por perfil e escopo de unidade |
