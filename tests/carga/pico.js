// Teste de carga do horário de pico (café da manhã) com k6.
// Cenários simultâneos:
//   - navegacao: clientes abrindo o cardápio (rota mais lida, com cache de saída)
//   - pedidos:   totens e app criando pedidos e solicitando pagamento
// Critérios (thresholds) = RNF01: p95 < 500 ms e taxa de erro < 1%.
//
// Execução (com o docker compose no ar):
//   docker run --rm --network raizes-do-nordeste_default -v "%CD%\tests\carga:/scripts" grafana/k6 run -e BASE_URL=http://api:8080 /scripts/pico.js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5000';
const SENHA = 'Raizes@2026';

const erroNegocio = new Rate('erros_inesperados');
const tempoPedido = new Trend('tempo_criar_pedido', true);

export const options = {
  scenarios: {
    navegacao: {
      executor: 'ramping-vus', exec: 'navegar', startVUs: 0,
      stages: [{ duration: '20s', target: 60 }, { duration: '40s', target: 60 }, { duration: '10s', target: 0 }],
    },
    pedidos: {
      executor: 'ramping-arrival-rate', exec: 'pedir', startRate: 2, timeUnit: '1s', preAllocatedVUs: 40, maxVUs: 80,
      stages: [{ duration: '20s', target: 15 }, { duration: '40s', target: 15 }, { duration: '10s', target: 0 }],
    },
  },
  thresholds: {
    'http_req_duration{scenario:navegacao}': ['p(95)<500'],
    'tempo_criar_pedido': ['p(95)<500'],
    'erros_inesperados': ['rate<0.01'],
  },
};

function login(email) {
  const r = http.post(`${BASE}/api/v1/auth/login`, JSON.stringify({ email, senha: SENHA }), { headers: { 'Content-Type': 'application/json' } });
  return r.json('accessToken');
}

export function setup() {
  const unidades = http.get(`${BASE}/api/v1/unidades`).json();
  const aeroporto = unidades.find((u) => u.nome.includes('Aeroporto'));
  const cardapio = http.get(`${BASE}/api/v1/unidades/${aeroporto.id}/cardapio`).json();
  // Abastece o estoque para o teste não ser limitado por falta de produto.
  const gerente = login('gerente.aeroporto@raizes.com.br');
  for (const item of cardapio.itens) {
    http.post(`${BASE}/api/v1/unidades/${aeroporto.id}/estoque/movimentacoes`,
      JSON.stringify({ produtoId: item.produtoId, tipo: 'Entrada', quantidade: 5000 }),
      { headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${gerente}` } });
  }
  return {
    unidade: aeroporto.id,
    produtos: cardapio.itens.map((i) => i.produtoId),
    // Cada usuário envia o canalPedido compatível com o seu perfil.
    usuarios: [
      { token: login('totem.aeroporto@raizes.com.br'), canal: 'TOTEM' },
      { token: login('maria@exemplo.com'), canal: 'APP' },
      { token: login('joao@exemplo.com'), canal: 'WEB' },
    ],
  };
}

export function navegar(dados) {
  const r = http.get(`${BASE}/api/v1/unidades/${dados.unidade}/cardapio`, { tags: { rota: 'cardapio' } });
  erroNegocio.add(r.status !== 200);
  check(r, { 'cardápio 200': (x) => x.status === 200 });
  sleep(Math.random() * 2 + 0.5);
}

export function pedir(dados) {
  const { token, canal } = dados.usuarios[Math.floor(Math.random() * dados.usuarios.length)];
  const produto = dados.produtos[Math.floor(Math.random() * dados.produtos.length)];
  const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}`, 'Idempotency-Key': `${__VU}-${__ITER}-${Date.now()}` };
  const r = http.post(`${BASE}/api/v1/pedidos`, JSON.stringify({ canalPedido: canal, unidadeId: dados.unidade, itens: [{ produtoId: produto, quantidade: 1 }] }),
    { headers, tags: { rota: 'criar_pedido' } });
  tempoPedido.add(r.timings.duration);
  erroNegocio.add(r.status !== 201);
  if (!check(r, { 'pedido 201': (x) => x.status === 201 })) return;

  const p = http.post(`${BASE}/api/v1/pedidos/${r.json('id')}/pagamentos`, JSON.stringify({ metodo: 'Pix' }),
    { headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }, tags: { rota: 'pagamento' } });
  erroNegocio.add(p.status !== 202);
  check(p, { 'pagamento 202': (x) => x.status === 202 });
}

export function handleSummary(data) {
  return {
    '/scripts/resultado-pico.json': JSON.stringify(data, null, 2),
    stdout: textSummary(data),
  };
}

function textSummary(d) {
  const m = d.metrics;
  const linha = (nome, met) => met ? `${nome}: p95=${met.values['p(95)']?.toFixed(1)} ms, média=${met.values.avg?.toFixed(1)} ms\n` : '';
  return '\n==== Resumo do teste de pico ====\n' +
    `Requisições: ${m.http_reqs.values.count} (${m.http_reqs.values.rate.toFixed(1)}/s)\n` +
    linha('Todas as requisições', m.http_req_duration) +
    linha('Criar pedido', m.tempo_criar_pedido) +
    `Erros inesperados: ${(m.erros_inesperados.values.rate * 100).toFixed(2)}%\n` +
    `Thresholds: ${Object.entries(d.metrics).filter(([, v]) => v.thresholds).map(([k, v]) => `${k}=${Object.values(v.thresholds).every((t) => t.ok) ? 'OK' : 'FALHOU'}`).join(', ')}\n`;
}
