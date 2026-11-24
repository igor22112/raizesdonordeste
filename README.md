# Raízes do Nordeste — API (Trilha Back-end)

Back-end da rede de lanchonetes **Raízes do Nordeste** (Projeto Multidisciplinar — UNINTER, 2026).
Pedidos multicanal (campo `canalPedido`: App, Web, Totem, Balcao, Pickup), cardápio por unidade, estoque com
reserva atômica, pagamento por gateway externo **simulado (mock)**, fidelidade com LGPD, auditoria e relatórios
para a matriz.

## Links e evidências

| Item | Endereço |
|---|---|
| Repositório | https://github.com/igor22112/raizesdonordeste |
| Swagger / documentação interativa (local) | http://localhost:5000/scalar/v1 (rota `/scalar/v1`) |
| Contrato OpenAPI (local) | http://localhost:5000/openapi/v1.json |
| Coleção Postman/Insomnia | [`tests/postman/RaizesDoNordeste.postman_collection.json`](tests/postman/RaizesDoNordeste.postman_collection.json) |
| DER | [`docs/diagramas/der.png`](docs/diagramas/der.png) · script do banco [`docs/banco/schema.sql`](docs/banco/schema.sql) |
| Documento (PDF) | [`docs/documento/`](docs/documento/) |
| Relatórios de execução | [`docs/evidencias/`](docs/evidencias/) (Newman, testes automatizados, cobertura, carga) |

Fluxo crítico entregue: **Fluxo A — Pedido → Pagamento mock → Atualização de status**.

## 1. Requisitos

| Requisito | Versão | Observação |
|---|---|---|
| Docker Desktop (com Docker Compose) | 4.x / Compose v2 | **Único requisito para rodar tudo** (Opção 1) |
| .NET SDK | 10.0 | Só para rodar sem Docker ou executar os testes automatizados |
| SQL Server | 2022 | Já incluso no `docker-compose.yml` |
| Linguagem / framework | C# 14, ASP.NET Core 10, EF Core 10 | Dependências NuGet restauradas automaticamente |
| Postman ou Insomnia (opcional) | qualquer | Para executar a coleção de testes |

## 2. Variáveis de ambiente

As configurações sensíveis vêm de variáveis de ambiente. Um modelo comentado está em [`.env.example`](.env.example):

```bash
cp .env.example .env        # Windows (PowerShell/cmd): copy .env.example .env
```

Edite o `.env` se quiser trocar senhas e chaves. **Sem o `.env`, o Docker Compose usa os mesmos valores padrão
de desenvolvimento**, então este passo é opcional para avaliação. O `.env` não é versionado.

| Variável | Uso |
|---|---|
| `SQL_SA_PASSWORD` | Senha do SQL Server |
| `JWT_KEY` | Chave de assinatura do token (mín. 32 caracteres) |
| `GATEWAY_API_KEY`, `WEBHOOK_SECRET` | Integração com o gateway mock (API key e segredo HMAC do webhook) |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | Administrador criado no primeiro start |
| `RATE_LIMIT` | Requisições por usuário/IP a cada 10 s |

## 3. Instalar dependências

- **Com Docker:** nada a instalar; o build das imagens restaura os pacotes.
- **Sem Docker:** `dotnet restore` na raiz do repositório.

## 4. Banco de dados, migrations e seed

São **automáticos** na inicialização da API: ela cria o banco, aplica as migrations do EF Core
(`src/RaizesDoNordeste.Infrastructure/Persistencia/Migracoes`) e carrega o seed de demonstração (7 unidades em
5 regiões, 16 produtos, equipes por unidade, 3 clientes e 400 pedidos históricos).

Comandos manuais (opcionais, com o .NET SDK):

```bash
dotnet tool restore
dotnet ef database update --project src/RaizesDoNordeste.Infrastructure --startup-project src/RaizesDoNordeste.Api
```

Para recriar o banco do zero: `docker compose down -v` e suba de novo.

## 5. Iniciar a API

### Opção 1 — Docker (recomendado)

```bash
docker compose up -d --build
```

Aguarde ~30 s na primeira vez (SQL Server + migrations + seed) e confira: http://localhost:5000/health/ready deve
responder `Healthy`. Serviços: API em `:5000`, gateway mock em `:5100`, SQL Server em `:1433`.
Para parar: `docker compose down`.

### Opção 2 — Sem Docker para a API

```bash
docker compose up -d sqlserver                     # ou um SQL Server próprio (ajuste appsettings.Development.json)
dotnet run --project src/PaymentGateway.Mock       # porta 5100
dotnet run --project src/RaizesDoNordeste.Api      # porta 5000, ambiente Development
```

## 6. Documentação da API (Swagger/OpenAPI)

- Interface interativa: **http://localhost:5000/scalar/v1** — clique em *Authentication*, cole o `accessToken`
  obtido em `POST /api/v1/auth/login` e teste os endpoints.
- Contrato OpenAPI 3.1: http://localhost:5000/openapi/v1.json (importável no Postman/Insomnia).
- Exemplos de request/response, status codes e o padrão de erro estão no documento (seção 4).

**Padrão de erro** (todas as falhas): `application/problem+json` com `type`, `title`, `status`, `detail`,
`instance`, `codigo`, `timestamp`, `traceId` (e `erros` por campo em validações).

## 7. Contas de demonstração

Senha de todas: `Raizes@2026` (somente ambiente de demonstração).

| Perfil | E-mail |
|---|---|
| Administrador da matriz | `admin@raizes.com.br` |
| Cliente (nível Prata, fidelidade e campanhas) | `maria@exemplo.com` |
| Cliente (fidelidade, sem campanhas personalizadas) | `joao@exemplo.com` |
| Cliente (sem consentimento de fidelidade) | `ana@exemplo.com` |
| Equipe de uma unidade | `gerente.<unidade>@raizes.com.br`, `atendente.<unidade>@…`, `cozinha.<unidade>@…`, `totem.<unidade>@…` |

Unidades: `boaviagem`, `aeroporto` (Recife, **24h**, cozinha reduzida), `aldeota`, `pelourinho`, `paulista`,
`asasul`, `manaus`. Use a unidade **aeroporto** para testar a qualquer hora.

## 8. Rodar os testes

### Coleção Postman/Insomnia (evidência principal — 52 cenários T01–T52)

1. API no ar com o seed (passo 5, Opção 1).
2. Importe `tests/postman/RaizesDoNordeste.postman_collection.json`. A variável `baseUrl` já é `http://localhost:5000`.
3. Execute **a coleção inteira, em ordem** (Collection Runner). Pastas: `01 Auth`, `02 Unidades e Cardápio`,
   `03 Produtos e Estoque`, `04 Pedidos`, `05 Pagamento (mock) e Status`, `06 Fidelidade, Campanhas e LGPD`,
   `07 Relatórios e Auditoria`.
4. **Token:** não é preciso copiar nada. A pasta `01 Auth` faz login com cada perfil e guarda os tokens em
   variáveis da coleção (`token_admin`, `token_maria`, `token_totem`...), usadas pelas requisições seguintes.

Pela linha de comando (Newman):

```bash
docker run --rm --network raizes-do-nordeste_default -v "${PWD}/tests/postman:/etc/newman" postman/newman \
  run RaizesDoNordeste.postman_collection.json --env-var baseUrl=http://api:8080 --delay-request 150
```

> O login aceita 10 tentativas por minuto por IP. Se rodar a coleção duas vezes seguidas e receber 429, aguarde
> um minuto. Para resultados idênticos aos documentados, recrie o banco (`docker compose down -v; docker compose up -d`).

### Testes automatizados (182)

```bash
dotnet test     # 122 unitários + 60 de integração (sobe um SQL Server em container; requer Docker)
```

### Carga (horário de pico)

```bash
docker run --rm --network raizes-do-nordeste_default -v "${PWD}/tests/carga:/scripts" grafana/k6 run -e BASE_URL=http://api:8080 /scripts/pico.js
```

## Gateway de pagamento simulado

`src/PaymentGateway.Mock` é um serviço separado que imita um provedor real: responde 202, confirma de forma
assíncrona por **webhook assinado (HMAC-SHA256)** e respeita a `Idempotency-Key`. Valores especiais:

| Total do pedido | Comportamento |
|---|---|
| R$ 13,13 | Pagamento **recusado** (webhook `recusado`) |
| R$ 14,14 | Recusa imediata do gateway |
| R$ 77,77 | Primeira chamada lenta (timeout → re-tentativa sem cobrança duplicada) |
| R$ 99,99 | Gateway fora do ar (API responde 503 + circuit breaker) |
| demais | **Aprovado** via webhook |

## Estrutura (camadas)

```
src/
  RaizesDoNordeste.Domain          Domain: entidades e regras (Pedido, Produto, Estoque, Cliente...), sem dependências
  RaizesDoNordeste.Application     Application: casos de uso (criar pedido, pagamento, fidelidade...), validações, portas
  RaizesDoNordeste.Infrastructure  Infrastructure: EF Core/SQL Server, migrations, seed, JWT, cliente do gateway, jobs
  RaizesDoNordeste.Api             API: controllers, autenticação/autorização, padrão de erro, OpenAPI
  PaymentGateway.Mock              serviço externo de pagamento (mock)
tests/
  RaizesDoNordeste.UnitTests, RaizesDoNordeste.IntegrationTests, postman/, carga/
docs/
  adr/ (decisões de arquitetura), diagramas/, banco/, documento/, evidencias/
```

## Segredos

Os valores em `appsettings.Development.json`, `docker-compose.yml` e `.env.example` são **apenas de
desenvolvimento**. Em produção, use variáveis de ambiente ou cofre de segredos.
