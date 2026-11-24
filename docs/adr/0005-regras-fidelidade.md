# ADR-0005 — Regras de fidelidade e descontos

**Status:** aceita (valores são premissas do projeto, parametrizados em `RegrasFidelidade`)

## Contexto
O estudo pede acúmulo de pontos, descontos progressivos e campanhas segmentadas, mas não define valores.

## Decisão
| Regra | Valor |
|---|---|
| Acúmulo | 1 ponto por real pago (arredondado para baixo), creditado quando o pedido é **Entregue** |
| Resgate | 1 ponto = R$ 0,05 (100 pontos = R$ 5,00), debitado na criação do pedido; devolvido se cancelado/expirado |
| Níveis (pedidos entregues em 90 dias) | Bronze 0–4 (0%), Prata 5–14 (3%), Ouro 15+ (5%) |
| Campanha × nível | **Não cumulativos**: vale o maior percentual |
| Pontos × percentual | Cumulativos |
| Teto | Soma dos descontos ≤ 50% do subtotal; só os pontos efetivamente usados são debitados |
| Participação | Só clientes identificados com consentimento `ProgramaFidelidade` |

## Consequências
- Pontos não são creditados em pedidos cancelados (crédito só na entrega).
- Todo desconto aplicado gera registro de auditoria `DescontoAplicado` (rastreabilidade exigida pela matriz).
- Cálculo concentrado em `CalculadoraPedido` (função pura, coberta por testes unitários).
