# Uso: python tests/postman/gerar_colecao.py
# Gera tests/postman/RaizesDoNordeste.postman_collection.json (Postman v2.1).
# Cada requisição tem o ID do cenário do plano de testes (Txx) no nome: o documento referencia esses nomes.
# Pré-requisito para executar: ambiente com dados de demonstração (docker compose up -d) e execução em ordem.
import json, os, uuid

def req(nome, metodo, url, corpo=None, testes=None, auth="{{token_admin}}", headers=None, prerequest=None, corpo_bruto=None):
    h = [{"key": "Content-Type", "value": "application/json"}]
    if headers: h += [{"key": k, "value": v} for k, v in headers.items()]
    caminho, _, query = url.partition("?")
    u = {"raw": "{{baseUrl}}" + url, "host": ["{{baseUrl}}"], "path": caminho.strip("/").split("/")}
    if query: u["query"] = [{"key": kv.split("=")[0], "value": kv.split("=")[1]} for kv in query.split("&")]
    item = {"name": nome, "request": {"method": metodo, "header": h, "url": u}, "event": []}
    item["request"]["auth"] = {"type": "bearer", "bearer": [{"key": "token", "value": auth, "type": "string"}]} if auth else {"type": "noauth"}
    if corpo is not None or corpo_bruto is not None:
        raw = corpo_bruto if corpo_bruto is not None else json.dumps(corpo, ensure_ascii=False, indent=2)
        item["request"]["body"] = {"mode": "raw", "raw": raw, "options": {"raw": {"language": "json"}}}
    if testes: item["event"].append({"listen": "test", "script": {"type": "text/javascript", "exec": testes.strip().split("\n")}})
    if prerequest: item["event"].append({"listen": "prerequest", "script": {"type": "text/javascript", "exec": prerequest.strip().split("\n")}})
    return item

def status(code, extra=""):
    return f'pm.test("HTTP {code}", () => pm.response.to.have.status({code}));\n{extra}'

def erro(code, codigo, extra=""):
    # Padrão de erro único (ProblemDetails): verifica o formato além do status.
    return status(code, f"""const e = pm.response.json();
pm.test("codigo {codigo}", () => pm.expect(e.codigo).to.eql("{codigo}"));
pm.test("Formato de erro padrão", () => {{ pm.expect(e).to.include.all.keys("type","title","status","detail","instance","codigo","timestamp","traceId"); }});
{extra}""")

def login(nome, email, var, tid=None):
    titulo = f"{tid} - Login válido ({nome})" if tid else f"Login {nome}"
    return req(titulo, "POST", "/api/v1/auth/login", {"email": email, "senha": "{{senha}}"},
               status(200, f'pm.test("accessToken presente", () => pm.expect(pm.response.json().accessToken).to.be.a("string"));\npm.collectionVariables.set("{var}", pm.response.json().accessToken);'), auth=None)

ESPERA = "setTimeout(() => {}, 2500); // aguarda o webhook assíncrono do gateway"
CHAVE = {"Idempotency-Key": "{{$guid}}"}
pastas = []

pastas.append({"name": "01 Auth", "item": [
    login("admin da matriz", "admin@raizes.com.br", "token_admin", "T01"),
    login("cliente Maria", "maria@exemplo.com", "token_maria"),
    login("cliente Ana (sem fidelidade)", "ana@exemplo.com", "token_ana"),
    login("gerente aeroporto", "gerente.aeroporto@raizes.com.br", "token_gerente"),
    login("atendente aeroporto", "atendente.aeroporto@raizes.com.br", "token_atendente"),
    login("cozinha aeroporto", "cozinha.aeroporto@raizes.com.br", "token_cozinha"),
    login("totem aeroporto", "totem.aeroporto@raizes.com.br", "token_totem"),
    req("T02 - Sem token (401)", "GET", "/api/v1/pedidos", None, erro(401, "NAO_AUTENTICADO"), auth=None),
    req("T03 - Perfil sem permissão: cliente na auditoria (403)", "GET", "/api/v1/auditoria", None, erro(403, "ACESSO_NEGADO"), auth="{{token_maria}}"),
    req("T04 - Login com senha errada (401)", "POST", "/api/v1/auth/login", {"email": "maria@exemplo.com", "senha": "Errada123"},
        erro(401, "NAO_AUTENTICADO"), auth=None),
    req("T05 - Cadastrar cliente com consentimentos (201)", "POST", "/api/v1/auth/registro",
        {"nome": "Cliente Postman", "email": "postman-{{$timestamp}}@exemplo.com", "senha": "Senha@123", "aceiteTermos": True,
         "consentimentoFidelidade": True, "consentimentoCampanhasPersonalizadas": False},
        status(201, 'pm.test("Perfil Cliente", () => pm.expect(pm.response.json().perfil).to.eql("Cliente"));\npm.test("Não expõe hash de senha", () => pm.expect(pm.response.text()).to.not.include("senha"));'), auth=None),
    req("T06 - Cadastro com e-mail inválido (400)", "POST", "/api/v1/auth/registro",
        {"nome": "Formato ruim", "email": "nao-e-email", "senha": "Senha@123", "aceiteTermos": True,
         "consentimentoFidelidade": False, "consentimentoCampanhasPersonalizadas": False},
        erro(400, "VALIDACAO", 'pm.test("Aponta o campo Email", () => pm.expect(JSON.stringify(e.erros)).to.include("Email"));'), auth=None),
    req("T07 - Cadastro sem aceitar termos (400)", "POST", "/api/v1/auth/registro",
        {"nome": "Sem termos", "email": "semtermos-{{$timestamp}}@exemplo.com", "senha": "Senha@123", "aceiteTermos": False,
         "consentimentoFidelidade": False, "consentimentoCampanhasPersonalizadas": False}, erro(400, "VALIDACAO"), auth=None),
]})

pastas.append({"name": "02 Unidades e Cardápio", "item": [
    req("T08 - Listar unidades (200)", "GET", "/api/v1/unidades", None, status(200, """
const u = pm.response.json();
const aero = u.find(x => x.nome.includes("Aeroporto"));
const boa = u.find(x => x.nome.includes("Boa Viagem"));
pm.collectionVariables.set("unidade_aeroporto", aero.id);
pm.collectionVariables.set("unidade_boaviagem", boa.id);
pm.test("Aeroporto 24h aberta e com cozinha reduzida", () => { pm.expect(aero.abertaAgora).to.be.true; pm.expect(aero.tipo).to.eql("CozinhaReduzida"); });"""), auth=None),
    req("T09 - Cardápio da unidade (200)", "GET", "/api/v1/unidades/{{unidade_aeroporto}}/cardapio", None, status(200, """
const c = pm.response.json();
pm.collectionVariables.set("produto_tapioca", c.itens.find(i => i.nome.startsWith("Tapioca de carne")).produtoId);
pm.collectionVariables.set("produto_cafe", c.itens.find(i => i.nome.startsWith("Café coado")).produtoId);
pm.test("Sem produtos de cozinha completa", () => pm.expect(c.itens.some(i => i.nome.startsWith("Cuscuz"))).to.be.false);
pm.test("Sem produtos juninos fora de época", () => pm.expect(c.itens.some(i => i.nome === "Canjica")).to.be.false);"""), auth=None),
    req("Cardápio Boa Viagem (cozinha completa)", "GET", "/api/v1/unidades/{{unidade_boaviagem}}/cardapio", None, status(200, """
pm.collectionVariables.set("produto_cuscuz", pm.response.json().itens.find(i => i.nome.startsWith("Cuscuz nordestino")).produtoId);"""), auth=None),
    req("T10 - Liberar produto de cozinha completa em unidade reduzida (409)", "PUT", "/api/v1/unidades/{{unidade_aeroporto}}/cardapio/{{produto_cuscuz}}",
        {"disponivel": True}, erro(409, "PRODUTO_INCOMPATIVEL_UNIDADE"), auth="{{token_gerente}}"),
    req("T11 - Gerente alterando cardápio de outra unidade (403)", "PUT", "/api/v1/unidades/{{unidade_boaviagem}}/cardapio/{{produto_cuscuz}}",
        {"disponivel": True}, erro(403, "ACESSO_NEGADO"), auth="{{token_gerente}}"),
]})

pastas.append({"name": "03 Produtos e Estoque", "item": [
    req("T12 - Admin cadastra produto (201)", "POST", "/api/v1/produtos",
        {"nome": "Tapioca teste {{$timestamp}}", "categoria": "Tapioca", "precoBase": 13.13, "requerCozinhaCompleta": False},
        status(201, 'pm.collectionVariables.set("produto_teste", pm.response.json().id);')),
    req("Admin libera o produto no cardápio do aeroporto", "PUT", "/api/v1/unidades/{{unidade_aeroporto}}/cardapio/{{produto_teste}}",
        {"disponivel": True}, status(200)),
    req("T13 - Entrada de estoque (201)", "POST", "/api/v1/unidades/{{unidade_aeroporto}}/estoque/movimentacoes",
        {"produtoId": "{{produto_teste}}", "tipo": "Entrada", "quantidade": 3}, status(201, 'pm.test("Saldo 3", () => pm.expect(pm.response.json().disponivel).to.eql(3));'), auth="{{token_gerente}}"),
    req("T14 - Consultar saldo da unidade (200)", "GET", "/api/v1/unidades/{{unidade_aeroporto}}/estoque", None,
        status(200, 'pm.test("Produto teste com 3 disponíveis", () => pm.expect(pm.response.json().find(i => i.produtoId === pm.collectionVariables.get("produto_teste")).disponivel).to.eql(3));'), auth="{{token_gerente}}"),
    req("T15 - Perda sem motivo (400)", "POST", "/api/v1/unidades/{{unidade_aeroporto}}/estoque/movimentacoes",
        {"produtoId": "{{produto_cafe}}", "tipo": "Perda", "quantidade": 2}, erro(400, "VALIDACAO"), auth="{{token_gerente}}"),
    req("T16 - Registrar perda com motivo (auditada) (201)", "POST", "/api/v1/unidades/{{unidade_aeroporto}}/estoque/movimentacoes",
        {"produtoId": "{{produto_cafe}}", "tipo": "Perda", "quantidade": 1, "motivo": "Validade vencida"}, status(201), auth="{{token_gerente}}"),
    req("T17 - Gerente consultando estoque de outra unidade (403)", "GET", "/api/v1/unidades/{{unidade_boaviagem}}/estoque", None, erro(403, "ACESSO_NEGADO"), auth="{{token_gerente}}"),
]})

pastas.append({"name": "04 Pedidos", "item": [
    req("Fidelidade antes do pedido", "GET", "/api/v1/clientes/me/fidelidade", None, status(200, """
pm.collectionVariables.set("saldo_antes", pm.response.json().saldoPontos);
pm.test("Maria é nível Prata", () => pm.expect(pm.response.json().nivel).to.eql("Prata"));"""), auth="{{token_maria}}"),
    req("T18 - Criar pedido válido pelo APP com resgate de pontos (201)", "POST", "/api/v1/pedidos",
        {"canalPedido": "APP", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_tapioca}}", "quantidade": 2}], "pontosResgatar": 40},
        status(201, """
const p = pm.response.json();
pm.collectionVariables.set("pedido_app", p.id);
pm.test("canalPedido App", () => pm.expect(p.canalPedido).to.eql("App"));
pm.test("AguardandoPagamento", () => pm.expect(p.status).to.eql("AguardandoPagamento"));
pm.test("Itens, valores e descontos", () => { pm.expect(p.itens.length).to.eql(1); pm.expect(p.descontoNivel).to.be.above(0); pm.expect(p.descontoPontos).to.eql(2); pm.expect(p.total).to.be.below(p.subtotal); });"""),
        auth="{{token_maria}}", headers={"Idempotency-Key": "{{chave_pedido}}"},
        prerequest='pm.collectionVariables.set("chave_pedido", pm.variables.replaceIn("{{$guid}}"));'),
    req("T19 - Reenvio com a mesma Idempotency-Key não duplica (200)", "POST", "/api/v1/pedidos",
        {"canalPedido": "APP", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_tapioca}}", "quantidade": 2}], "pontosResgatar": 40},
        status(200, 'pm.test("Mesmo pedido", () => pm.expect(pm.response.json().id).to.eql(pm.collectionVariables.get("pedido_app")));'),
        auth="{{token_maria}}", headers={"Idempotency-Key": "{{chave_pedido}}"}),
    req("T20 - Totem cria pedido com canalPedido TOTEM (201)", "POST", "/api/v1/pedidos",
        {"canalPedido": "TOTEM", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_cafe}}", "quantidade": 1}], "nomeChamada": "Senha 42"},
        status(201, 'pm.collectionVariables.set("pedido_totem", pm.response.json().id);\npm.test("canalPedido Totem", () => pm.expect(pm.response.json().canalPedido).to.eql("Totem"));'),
        auth="{{token_totem}}", headers=CHAVE),
    req("T21 - Pedido sem canalPedido (400)", "POST", "/api/v1/pedidos",
        {"unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_cafe}}", "quantidade": 1}]},
        erro(400, "VALIDACAO", 'pm.test("Aponta canalPedido", () => pm.expect(JSON.stringify(e.erros)).to.include("CanalPedido"));'), auth="{{token_totem}}", headers=CHAVE),
    req("T22 - Pedido com canalPedido inválido (400)", "POST", "/api/v1/pedidos",
        {"canalPedido": "FAX", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_cafe}}", "quantidade": 1}]},
        erro(400, "VALIDACAO"), auth="{{token_totem}}", headers=CHAVE),
    req("T23 - Cliente declarando canal TOTEM (409)", "POST", "/api/v1/pedidos",
        {"canalPedido": "TOTEM", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_cafe}}", "quantidade": 1}]},
        erro(409, "CANAL_INCOMPATIVEL"), auth="{{token_maria}}", headers=CHAVE),
    req("T24 - Quantidade negativa (400)", "POST", "/api/v1/pedidos",
        {"canalPedido": "TOTEM", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_cafe}}", "quantidade": -1}]},
        erro(400, "VALIDACAO"), auth="{{token_totem}}", headers=CHAVE),
    req("T25 - Pedido com produto inexistente (404)", "POST", "/api/v1/pedidos",
        {"canalPedido": "TOTEM", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "00000000-0000-0000-0000-000000000001", "quantidade": 1}]},
        erro(404, "NAO_ENCONTRADO"), auth="{{token_totem}}", headers=CHAVE),
    req("T26 - Pedido com unidade inexistente (404)", "POST", "/api/v1/pedidos",
        {"canalPedido": "APP", "unidadeId": "00000000-0000-0000-0000-000000000001", "itens": [{"produtoId": "{{produto_cafe}}", "quantidade": 1}]},
        erro(404, "NAO_ENCONTRADO"), auth="{{token_maria}}", headers=CHAVE),
    req("T27 - Pedido com estoque insuficiente (409)", "POST", "/api/v1/pedidos",
        {"canalPedido": "TOTEM", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_teste}}", "quantidade": 4}]},
        erro(409, "ESTOQUE_INSUFICIENTE"), auth="{{token_totem}}", headers=CHAVE),
    req("T28 - Sem consentimento de fidelidade, resgatar pontos (409)", "POST", "/api/v1/pedidos",
        {"canalPedido": "APP", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_cafe}}", "quantidade": 1}], "pontosResgatar": 10},
        erro(409, "FIDELIDADE_SEM_CONSENTIMENTO"), auth="{{token_ana}}", headers=CHAVE),
    req("T29 - Listar pedidos filtrando por canal e status, paginado (200)", "GET", "/api/v1/pedidos?canalPedido=TOTEM&status=AguardandoPagamento&page=1&limit=10", None,
        status(200, """
const r = pm.response.json();
pm.test("Só pedidos de totem", () => r.itens.forEach(p => pm.expect(p.canalPedido).to.eql("Totem")));
pm.test("Contém o pedido do totem", () => pm.expect(r.itens.map(p => p.id)).to.include(pm.collectionVariables.get("pedido_totem")));
pm.test("Paginação", () => { pm.expect(r.pagina).to.eql(1); pm.expect(r.tamanho).to.eql(10); });"""), auth="{{token_gerente}}"),
    req("T30 - Cancelar pedido não pago (200)", "POST", "/api/v1/pedidos/{{pedido_totem}}/cancelamento", {"motivo": "Cliente desistiu no totem"},
        status(200, 'pm.test("Cancelado", () => pm.expect(pm.response.json().status).to.eql("Cancelado"));'), auth="{{token_totem}}"),
]})

pastas.append({"name": "05 Pagamento (mock) e Status", "item": [
    req("T31 - Solicitar pagamento ao gateway mock (202)", "POST", "/api/v1/pedidos/{{pedido_app}}/pagamentos", {"metodo": "Pix"},
        status(202, 'pm.test("Pagamento Pendente", () => pm.expect(pm.response.json().status).to.eql("Pendente"));'), auth="{{token_maria}}"),
    req("T32 - Pagamento aprovado atualiza o pedido para Pago (200)", "GET", "/api/v1/pedidos/{{pedido_app}}", None, status(200, """
const p = pm.response.json();
pm.test("Pedido Pago", () => pm.expect(p.status).to.eql("Pago"));
pm.test("Pagamento Aprovado", () => pm.expect(p.pagamentos[p.pagamentos.length - 1].status).to.eql("Aprovado"));"""), auth="{{token_maria}}", prerequest=ESPERA),
    req("T33 - Cozinha: EmPreparo (200)", "PATCH", "/api/v1/pedidos/{{pedido_app}}/status", {"status": "EmPreparo"}, status(200), auth="{{token_cozinha}}"),
    req("T34 - Cozinha: Pronto (200)", "PATCH", "/api/v1/pedidos/{{pedido_app}}/status", {"status": "Pronto"}, status(200), auth="{{token_cozinha}}"),
    req("T35 - Cozinha tentando entregar (403)", "PATCH", "/api/v1/pedidos/{{pedido_app}}/status", {"status": "Entregue"}, erro(403, "ACESSO_NEGADO"), auth="{{token_cozinha}}"),
    req("T36 - Atendente: Entregue, com histórico completo (200)", "PATCH", "/api/v1/pedidos/{{pedido_app}}/status", {"status": "Entregue"}, status(200, """
pm.test("Histórico", () => pm.expect(pm.response.json().historico.map(x => x.para)).to.eql(["AguardandoPagamento","Pago","EmPreparo","Pronto","Entregue"]));"""), auth="{{token_atendente}}"),
    req("T37 - Pontos creditados na entrega (200)", "GET", "/api/v1/clientes/me/fidelidade", None, status(200, """
pm.test("Saldo aumentou além do resgate", () => pm.expect(pm.response.json().saldoPontos).to.be.above(Number(pm.collectionVariables.get("saldo_antes")) - 40));"""), auth="{{token_maria}}"),
    req("Totem cria pedido de R$ 13,13 (valor que o mock recusa)", "POST", "/api/v1/pedidos",
        {"canalPedido": "TOTEM", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_teste}}", "quantidade": 1}]},
        status(201, 'pm.collectionVariables.set("pedido_recusa", pm.response.json().id);\npm.test("Total 13.13", () => pm.expect(pm.response.json().total).to.eql(13.13));'),
        auth="{{token_totem}}", headers=CHAVE),
    req("T38 - Pagamento recusado pelo mock (202 e depois Recusado)", "POST", "/api/v1/pedidos/{{pedido_recusa}}/pagamentos", {"metodo": "CartaoCredito"},
        status(202), auth="{{token_totem}}"),
    req("T39 - Pedido recusado continua aguardando, com motivo (200)", "GET", "/api/v1/pedidos/{{pedido_recusa}}", None, status(200, """
const p = pm.response.json(); const pg = p.pagamentos[p.pagamentos.length - 1];
pm.test("Pagamento Recusado", () => pm.expect(pg.status).to.eql("Recusado"));
pm.test("Mensagem do gateway", () => pm.expect(pg.detalhe).to.be.a("string").and.not.empty);
pm.test("Pedido segue AguardandoPagamento", () => pm.expect(p.status).to.eql("AguardandoPagamento"));"""), auth="{{token_totem}}", prerequest=ESPERA),
    req("Balcão cria pedido", "POST", "/api/v1/pedidos",
        {"canalPedido": "BALCAO", "unidadeId": "{{unidade_aeroporto}}", "itens": [{"produtoId": "{{produto_tapioca}}", "quantidade": 1}]},
        status(201, 'pm.collectionVariables.set("pedido_balcao", pm.response.json().id);'), auth="{{token_atendente}}", headers=CHAVE),
    req("Balcão paga com cartão", "POST", "/api/v1/pedidos/{{pedido_balcao}}/pagamentos", {"metodo": "CartaoDebito"}, status(202), auth="{{token_atendente}}"),
    req("T40 - Cancelar pedido pago estorna no gateway (200)", "POST", "/api/v1/pedidos/{{pedido_balcao}}/cancelamento", {"motivo": "Item errado"},
        status(200, """
const p = pm.response.json();
pm.test("Cancelado", () => pm.expect(p.status).to.eql("Cancelado"));
pm.test("Estornado", () => pm.expect(p.pagamentos[p.pagamentos.length - 1].status).to.eql("Estornado"));"""), auth="{{token_atendente}}", prerequest=ESPERA),
    req("T41 - Webhook sem assinatura HMAC (401)", "POST", "/api/v1/webhooks/pagamentos",
        {"referencia": "00000000-0000-0000-0000-000000000001", "transacaoId": "falso", "status": "aprovado"}, erro(401, "NAO_AUTENTICADO"), auth=None),
]})

pastas.append({"name": "06 Fidelidade, Campanhas e LGPD", "item": [
    req("T42 - Revogar consentimento de campanhas (200)", "PUT", "/api/v1/clientes/me/consentimentos", {"tipo": "CampanhasPersonalizadas", "concedido": False},
        status(200, 'pm.test("Revogado", () => pm.expect(pm.response.json().concedido).to.be.false);'), auth="{{token_maria}}"),
    req("Conceder novamente", "PUT", "/api/v1/clientes/me/consentimentos", {"tipo": "CampanhasPersonalizadas", "concedido": True}, status(200), auth="{{token_maria}}"),
    req("T43 - Revogar termos de uso isoladamente (409)", "PUT", "/api/v1/clientes/me/consentimentos", {"tipo": "TermosDeUso", "concedido": False},
        erro(409, "TERMOS_OBRIGATORIOS"), auth="{{token_maria}}"),
    req("T44 - Exportar meus dados (portabilidade LGPD) (200)", "GET", "/api/v1/clientes/me/dados", None, status(200, """
pm.test("Cadastro, pedidos e consentimentos", () => { const d = pm.response.json(); pm.expect(d.cadastro.email).to.eql("maria@exemplo.com"); pm.expect(d.pedidos.length).to.be.above(0); pm.expect(d.historicoConsentimentos.length).to.be.above(0); });"""),
        auth="{{token_maria}}"),
    req("T45 - Admin cria campanha regional (201)", "POST", "/api/v1/campanhas",
        {"nome": "Semana do Cuscuz Nordeste", "percentualDesconto": 10, "inicioEm": "{{agora}}", "fimEm": "{{daqui7dias}}", "regiao": "Nordeste"},
        status(201, 'pm.collectionVariables.set("campanha", pm.response.json().id);'),
        prerequest='pm.collectionVariables.set("agora", new Date(Date.now() - 60000).toISOString()); pm.collectionVariables.set("daqui7dias", new Date(Date.now()+7*864e5).toISOString());'),
    req("T46 - Campanhas elegíveis para a Maria (200)", "GET", "/api/v1/clientes/me/campanhas?unidadeId={{unidade_aeroporto}}", None, status(200, """
pm.test("Campanha listada", () => pm.expect(pm.response.json().map(c => c.id)).to.include(pm.collectionVariables.get("campanha")));"""), auth="{{token_maria}}"),
]})

pre_datas = 'const d = new Date(Date.now() - 3*36e5); pm.collectionVariables.set("hoje", d.toISOString().slice(0,10)); pm.collectionVariables.set("inicio", new Date(d - 60*864e5).toISOString().slice(0,10));'
pastas.append({"name": "07 Relatórios e Auditoria", "item": [
    req("T47 - Vendas consolidadas por canal (200)", "GET", "/api/v1/relatorios/vendas?de={{inicio}}&ate={{hoje}}&agrupamento=Canal", None,
        status(200, 'pm.test("Há vendas por canal", () => pm.expect(pm.response.json().linhas.length).to.be.above(1));'), prerequest=pre_datas),
    req("Vendas por região", "GET", "/api/v1/relatorios/vendas?de={{inicio}}&ate={{hoje}}&agrupamento=Regiao", None, status(200)),
    req("Vendas por unidade", "GET", "/api/v1/relatorios/vendas?de={{inicio}}&ate={{hoje}}&agrupamento=Unidade", None, status(200)),
    req("Produtos mais vendidos", "GET", "/api/v1/relatorios/produtos-mais-vendidos?de={{inicio}}&ate={{hoje}}&top=5", None, status(200)),
    req("T48 - Gerente no consolidado por região (403)", "GET", "/api/v1/relatorios/vendas?de={{inicio}}&ate={{hoje}}&agrupamento=Regiao", None,
        erro(403, "ACESSO_NEGADO"), auth="{{token_gerente}}"),
    req("T49 - Criação do pedido gerou registro de auditoria (200)", "GET", "/api/v1/auditoria?acao=PedidoCriado&entidadeId={{pedido_app}}", None, status(200, """
const r = pm.response.json();
pm.test("1 registro PedidoCriado", () => pm.expect(r.total).to.eql(1));
pm.test("Registra usuário e perfil", () => { pm.expect(r.itens[0].usuarioId).to.be.a("string"); pm.expect(r.itens[0].perfil).to.eql("Cliente"); });"""),
        prerequest="setTimeout(() => {}, 300);"),
    req("T50 - Mudanças de status do pedido auditadas (200)", "GET", "/api/v1/auditoria?acao=PedidoStatusAlterado&entidadeId={{pedido_app}}", None, status(200, """
pm.test("4 mudanças (Pago, EmPreparo, Pronto, Entregue)", () => pm.expect(pm.response.json().total).to.eql(4));""")),
    req("T51 - Cancelamento e estorno auditados (200)", "GET", "/api/v1/auditoria?acao=PedidoCancelado&entidadeId={{pedido_balcao}}", None, status(200, """
pm.test("Cancelamento com motivo", () => { const r = pm.response.json(); pm.expect(r.total).to.eql(1); pm.expect(r.itens[0].detalhes).to.include("Item errado"); });""")),
    req("T52 - Health check (readiness) (200)", "GET", "/health/ready", None, status(200), auth=None),
]})

colecao = {
    "info": {"_postman_id": str(uuid.uuid5(uuid.NAMESPACE_URL, "raizes-do-nordeste")), "name": "Raízes do Nordeste - API",
             "description": ("Coleção de evidências da trilha back-end. Os IDs (T01, T02...) correspondem ao plano de testes do documento.\n"
                             "Pré-requisitos: ambiente no ar com dados de demonstração (docker compose up -d --build).\n"
                             "Execução: Runner do Postman ou Newman, em ordem (as requisições guardam tokens e IDs em variáveis da coleção).\n"
                             "Variável baseUrl: http://localhost:5000."),
             "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"},
    "item": pastas,
    "variable": [{"key": "baseUrl", "value": "http://localhost:5000"}, {"key": "senha", "value": "Raizes@2026"}],
}
destino = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(destino, "RaizesDoNordeste.postman_collection.json"), "w", encoding="utf-8") as f:
    json.dump(colecao, f, ensure_ascii=False, indent=2)
print("requisicoes:", sum(len(p["item"]) for p in pastas), "| cenarios T:", sum(1 for p in pastas for i in p["item"] if i["name"][:1] == "T"))
