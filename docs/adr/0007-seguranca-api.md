# ADR-0007 — Autenticação JWT, autorização por perfil e escopo de unidade

**Status:** aceita

## Decisão
- **JWT HS256** (60 min) com claims `sub`, `role` e `unidade_id`; sem dados pessoais no token. Chave por variável
  de ambiente (mínimo 32 caracteres, validada na inicialização).
- **Perfis**: Cliente, Atendente, Cozinha, Gerente, Totem (conta de dispositivo) e AdminMatriz.
- **Duas barreiras de autorização**: `[Authorize(Roles=...)]` nos controllers e verificação de **escopo de unidade**
  nos casos de uso (gerente/atendente/cozinha/totem só operam a própria unidade; matriz vê tudo).
- Pedido de terceiro responde **404** (não revela existência); recurso de outra unidade responde **403**.
- **`canalPedido` obrigatório e conferido contra o perfil** (409 `CANAL_INCOMPATIVEL`): impede um cliente de se passar por totem/balcão.
- Senhas com PBKDF2 (ASP.NET Core Identity `PasswordHasher`), política mínima (8+ caracteres, letra e número).
- **Rate limiting**: token bucket por usuário/IP (global) e janela fixa por IP no login (anti força bruta) → 429.
- Validação de entrada com FluentValidation; erros em **ProblemDetails** (RFC 9457) com `codigo` estável e
  `correlacaoId`, sem stack trace.
- TLS terminado no balanceador/ingress (a API roda em HTTP dentro da rede privada); `ForwardedHeaders` habilitado.
