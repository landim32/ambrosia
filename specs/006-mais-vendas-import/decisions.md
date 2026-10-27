# Decisões do autopiloto — 006-mais-vendas-import

Execução 20261027-112248 · história 1/1 "004 — Importação do +Vendas pelo frontend (pedidos, clientes e fotos)"

## clarify · 2026-10-27 14:27
**Pergunta:** Em que situação uma execução termina como "parcial", e não como "concluída" ou "falhou"? Hoje a spec lista o status "parcial" (FR-047), mas não diz quando ele se aplica. Isso define o que a tela destaca e o que os testes de aceitação verificam.
**Opções:** Itens com erro (Recommended) (recomendada) · Só pedidos com erro · Blocos com sucesso + falho · Remover "parcial"
**Escolha:** Itens com erro (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela também se alinha à história: o status parcial sinaliza que todos os blocos terminaram, mas houve itens com erro, como CPF inválido, conflito com pedido manual ou imagem com erro. Já 'falhou' fica reservado para bloco falho, como nos critérios 11 e 29.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-27 14:28
**Pergunta:** Se a vendedora editar no Ambrosio um pedido que veio do +Vendas, o que deve acontecer com essa edição quando o período for importado de novo e a origem tiver outro conteúdo? Hoje FR-023 atualiza o pedido com os dados da origem, mas não diz nada sobre edições feitas à mão. Isso afeta o modelo de dados e os testes de idempotência.
**Opções:** Origem prevalece (Recommended) (recomendada) · Bloquear edição · Edição manual prevalece
**Escolha:** Origem prevalece (Recommended)
**Por quê:** A opção 1 está marcada como recomendada e é coerente com a história, que trata a importação como espelho idempotente do +Vendas (upsert por chave natural com substituição de itens e registro dos campos alterados). Os campos que existem só no Ambrosio são preservados, sem exigir rastreamento de edições manuais nem mudar a tela de pedido.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-27 14:28
**Pergunta:** O Ambrosio deve limitar as tentativas seguidas de importação com credenciais recusadas pelo +Vendas? Sem limite, a tela pode virar um jeito de testar senhas contra o +Vendas, e a conta da vendedora pode ser bloqueada lá. Isso define uma regra a mais e um teste de API.
**Opções:** 3 falhas / 15 min (Recommended) (recomendada) · 5 falhas / 1 hora · Sem limite
**Escolha:** 3 falhas / 15 min (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela protege a conta da vendedora no +Vendas e evita que a tela sirva para testar senhas, com uma regra simples e um teste de API.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-27 14:28
**Pergunta:** O que o Ambrosio deve fazer se a vendedora importar usando uma conta do +Vendas diferente da que usou antes (por exemplo, a conta de uma colega)? Os pedidos dessa conta entrariam na base dela, misturando vendas de outra pessoa. Proteger contra isso exige guardar uma identificação da conta, mas nunca o login em si.
**Opções:** Bloquear outra conta (Recommended) (recomendada) · Avisar e seguir · Não verificar
**Escolha:** Bloquear outra conta (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela evita misturar vendas de outra conta na base da vendedora, guardando apenas um hash com pepper do identificador, nunca o login em si.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-27 14:50
**Pergunta:** Quer que eu sugira edições concretas para os principais problemas encontrados? (Nada é aplicado sem sua aprovação.)
**Opções:** Críticos e altos (Recomendado) (recomendada) · Todos até MEDIUM · Não, só o relatório
**Escolha:** Críticos e altos (Recomendado)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela cobre os problemas críticos e altos (C1, I1, I2, I3) com edições concretas, sem ampliar o escopo. Nada é aplicado sem aprovação posterior da usuária.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-27 14:52
**Pergunta:** Quer que eu sugira edições concretas para os principais problemas restantes? Nada é aplicado sem sua aprovação.
**Opções:** Altos + F4 (Recommended) (recomendada) · Todos até MEDIUM · Não, só o relatório
**Escolha:** Altos + F4 (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela também é a que menos amplia escopo, limitando-se aos problemas de maior severidade e à referência pendente F4. Nada é aplicado sem aprovação do usuário.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-27 14:53
**Pergunta:** Quer que eu aplique tudo ou só uma parte, por exemplo só o I1 e o F4?
**Escolha:** Aplique tudo, não apenas o I1 e o F4.
**Por quê:** Nenhuma opção foi marcada como recomendada, então vale a que menos amplia escopo e risco. Aplicar apenas uma parte deixaria achados da análise sem correção e inconsistências entre spec, plano e tarefas. Aplicar todos os ajustes mantém os artefatos coerentes e não inventa nada fora do que a análise apontou.
_claude-code / claude-sonnet-5-5_

## implement · 2026-10-27
**Pergunta:** Até onde ir nesta execução e quais credenciais usar nos testes?
**Escolha:** Tudo (T001–T113) com validação automatizada; T114 (validação manual do quickstart) fica pendente. Só placeholders do coletor Fake (`s482+ok`, `v1`, `recusada`…) — nenhum login/senha real do +Vendas em código, Bruno ou teste.
**Por quê:** Escolha do usuário. T114 exige interação humana na tela.

## implement · 2026-10-27 · ajustes de implementação
- **Chromium na imagem (T106)**: o estágio `playwright` parte da camada de `restore` e roda o driver que o próprio pacote NuGet traz (`.playwright/node/linux-x64/node` + `package/cli.js`), sem instalar PowerShell. Assim o download (~260 MB) só invalida quando a versão fixa do `Microsoft.Playwright` muda. O CDN `playwright.azureedge.net` do Playwright 1.49 não responde mais; o download funciona pelo espelho `playwright-akamai.azureedge.net` após 30 s de timeout. Atualizar o pacote para uma versão que use `cdn.playwright.dev` fica como melhoria.
- **`System.Text.Json` 9.0.22 explícito no Infra**: o Playwright 1.49 puxa a 9.x e gerava MSB3277 contra a 8.0 do framework.
- **Provider sem carga automática**: o `ImportProvider` vive em `main.tsx` (também em `/login`), então quem chama `loadDefaults` é a `ImportPage`; ao desmontar ela pausa o polling.
- **Relatório final**: `runs/current` responde 204 quando a execução sai de ativa; o contexto guarda o id acompanhado e busca `runs/{id}` para mostrar o relatório final.
- **Tipos do frontend**: uniões inline nas interfaces (`ImportRun['status']`) em vez de `type`, pela regra "sempre interface" do projeto.
- **Fotos do Fake**: um binário por SKU (`other.jpg` no SKU 1), senão a deduplicação por SHA-256 juntaria duas fotos e "4 SKUs → 4 fotos" deixaria de ser conferível.
- **Bugs achados pela suíte de API e corrigidos**: (1) reimportação com mudança falhava — `ApplyImported` tirava os itens antigos da coleção e o EF tentava anular a FK obrigatória; o repositório agora os marca como excluídos; (2) blocos não executados ficavam `pending` no banco ao fechar a execução — `Finish` só alterava a memória; o fechamento e o cancelamento em `queued` agora persistem os blocos.
- **`v2` do Fake**: o nome do cliente entra no `source_hash`, então a reimportação `v2` atualiza 8 pedidos (6 com valor/status + 2 do cliente renomeado).

## implement · 2026-10-27 · storage da homologação
**Pergunta:** O bucket `ambrosio` não existe na conta das chaves do `.env` (toda foto falha e ~12 testes fecham `partial`). Como seguir?
**Escolha:** O usuário cria/conserta o bucket; a suíte de API é reexecutada depois.
**Por quê:** Criar recurso na conta do DigitalOcean do usuário é ação externa que exige autorização dele.

## implement · 2026-10-27 · 415 em POST /images
- **UnauthorizedAccessTests**: o teste mandava JSON para `POST /images`, que tem `[Consumes("multipart/form-data")]`. O ASP.NET Core aplica essa restrição na seleção do endpoint, antes da autorização, então a resposta era 415 sem o token ser conferido. A API não vaza nada nesse caso; o teste passou a enviar multipart nessa rota. As 82 verificações de 401 passam.

## implement · 2026-10-27 · bucket da homologação corrigido
**Problema:** todas as fotos da importação caíam em "imagem com erro". O log da API mostrava `PutObject` falhando com `The specified bucket does not exist`.
**Causa:** as chaves do `.env` só enxergam o Space `emagine` (`nyc3`). `ambrosio` era a pasta pedida dentro do bucket e acabou também como nome do bucket.
**Escolha:** bucket `emagine` em Development e homologação (`appsettings.*.json`, `.env`, `.env.example`, `docker-compose.yml`). As chaves continuam em `ambrosio/users/{id}/images/...`. Produção (`ambrosia-prod`) não mudou.

## implement · 2026-10-27 · coletor Playwright ajustado ao +Vendas real
**Problema:** a primeira execução real parou no login (timeout esperando `input[type='email']`). O coletor tinha sido escrito sem acesso ao site.
**O que o site real mostrou:**
- Login em duas etapas: `#username` → "Próximo" → `input[type=password]` → "Entrar". Os arquivos da SPA ficam em `/static/`, e o guard antigo os abortava por rota.
- A tela usa uma API JSON em `api.mais-vendas.somalabs.com.br` com token Bearer da sessão: `salesperson/orders` (lista do período), `orders/client-data` (nome e CPF), `order-detail` (itens) e `order-detail-info` (nome e foto do produto). A SPA também chama `mais-vendas-api.somalabs.com.br` (menu, vendedora ativa).
- Cancelamento já vem como registro próprio (`status_evento = CANCELADO`, valor negativo); devolução é `channel = DEVOLUCAO`.
- As fotos ficam no CDN da VTEX da marca (`*.vteximg.com.br`), em miniatura 55×55.
- `/storeOmni` lista os pedidos da loja inteira, em outro serviço; os pedidos online da vendedora já vêm na lista dela com `channel = ONLINE`.
**Escolha:**
- O navegador só faz o login; a coleta lê a API JSON com o token mais recente que a SPA enviou (401 tenta de novo uma vez).
- Só a lista da vendedora é coletada, toda como origem `cota`; o canal da origem vai em `channel`.
- Contadores e total do bloco vêm da própria lista e são conferidos contra os pedidos montados.
- Guard com três regras: navegação (rotas da coleta), recursos da página (hosts do +Vendas) e fotos (hosts do +Vendas ou `*.vteximg.com.br`, sem token).
- A foto é baixada em 600×600 (troca do segmento `-55-55` da URL da VTEX).
**Verificado:** importação de 26/10/2026 com conta real: 2 pedidos (venda online e devolução), 2 clientes e 2 fotos, iguais à tela do +Vendas.
