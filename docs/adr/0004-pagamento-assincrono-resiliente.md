# ADR-0004 — Pagamento assíncrono: idempotência, resiliência, webhook assinado e outbox

**Status:** aceita

## Contexto
O sistema só solicita o pagamento, recebe o resultado, registra e atualiza o pedido (§5). Gateways reais
confirmam de forma assíncrona, podem ficar lentos ou fora do ar e reenviam notificações.

## Decisão
1. **Fluxo**: `POST /pedidos/{id}/pagamentos` cria o registro `Pagamento` (Pendente) e chama o gateway → **202**.
   O gateway confirma por **webhook** → pedido vai para `Pago` e a reserva de estoque vira baixa.
2. **Idempotência ponta a ponta**:
   - `Idempotency-Key` obrigatório na criação do pedido (reenvio devolve o mesmo pedido, sem reservar de novo).
   - O Id do `Pagamento` é a chave de idempotência enviada ao gateway: re-tentativas não cobram duas vezes.
   - Índice único filtrado impede dois pagamentos ativos para o mesmo pedido.
   - Webhook repetido para pagamento já finalizado é ignorado (200).
3. **Resiliência no cliente HTTP** (`Microsoft.Extensions.Http.Resilience`/Polly): timeout por tentativa (3 s),
   2 re-tentativas com backoff exponencial e jitter, **circuit breaker** (50% de falhas em 30 s, mín. 5 chamadas,
   abre por 15 s) e timeout total (10 s). Gateway indisponível → **503 com Retry-After**; pedido continua
   aguardando pagamento (o cliente pode tentar de novo).
4. **Segurança do webhook**: HMAC-SHA256 sobre `"{timestamp}.{corpo}"`, comparação em tempo constante e
   janela de 5 minutos contra replay.
5. **Corridas tratadas**: aprovação que chega após cancelamento/expiração → **estorno automático**. Falha no
   estorno → `EstornoPendente`, registrado e auditado.
6. **Expiração**: pedido não pago em 15 min expira (job) e libera estoque e pontos.
7. **Outbox transacional**: eventos de domínio (`PedidoCriado`, `PedidoStatusAlterado`) são gravados na mesma
   transação e publicados por um job (entrega "pelo menos uma vez"), base para painel da cozinha, notificações e BI.

## Alternativas
- Pagamento síncrono bloqueante: simples, mas segura a requisição e falha mal quando o gateway está lento.
- Broker (RabbitMQ/Service Bus) desde já: robusto, porém infraestrutura extra; a outbox permite adicioná-lo depois.

## Consequências
- Testes: CT30–CT38 cobrem recusa assíncrona/síncrona, indisponibilidade + circuit breaker, timeout com
  re-tentativa sem cobrança duplicada, pagamento duplicado, webhook inválido/repetido e corrida com cancelamento.
