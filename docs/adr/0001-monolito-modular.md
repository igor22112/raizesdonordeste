# ADR-0001 — Monólito modular em camadas, com pagamento como serviço externo

**Status:** aceita

## Contexto
A rede está em expansão, opera em vários canais e exige alta disponibilidade no pico. A equipe é pequena e o
domínio ainda está em descoberta (regras de fidelidade, campanhas e variações regionais devem mudar).
Pedido, estoque e pontos precisam ser consistentes entre si (não vender sem estoque, não debitar pontos de
pedido que não existe). O estudo de caso determina que o pagamento é processado por serviços externos.

## Decisão
- Um único deployável (API) organizado em camadas: **Domain** (regras, sem dependências), **Application**
  (casos de uso), **Infrastructure** (banco, HTTP, jobs) e **Api** (HTTP). Os módulos (Pedidos, Estoque,
  Catálogo, Fidelidade, Privacidade, Relatórios) ficam separados por pasta/namespace dentro das camadas.
- O pagamento fica **fora**, atrás da porta `IGatewayPagamento`, com integração por HTTP + webhook.
- A API é **stateless** (JWT, sem sessão em memória): escala horizontalmente atrás de um balanceador.

## Alternativas consideradas
- **Microsserviços desde o início**: isolamento e deploy independente, mas exigiriam consistência eventual entre
  pedido/estoque/pontos (sagas), mais infraestrutura (broker, service discovery, tracing distribuído) e
  equipe maior. Custo alto para o estágio atual.
- **Monólito sem camadas**: mais rápido no início, mas mistura regra de negócio com HTTP/banco e dificulta testes.

## Consequências
- Transações locais garantem consistência de pedido + estoque + pontos.
- Domínio testável sem banco (testes unitários rápidos).
- Caminho de evolução: os módulos podem virar serviços quando houver necessidade real
  (candidatos: Pedidos/Pagamentos e Relatórios, que pode ler de uma réplica ou data warehouse).
- Risco: um bug pode afetar todos os módulos — mitigado por testes de integração e health checks.
