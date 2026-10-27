# Decisões do autopiloto — 002-frontend-web-clientes-pedidos

Execução 20261026-003621 · história 1/9 "1. História"

## specify · 2026-10-26 03:39
**Pergunta:** Q1 (FR-012): Como o CPF deve aparecer no perfil do cliente?
**Opções:** A - Mascarado com botão "mostrar" (Recommended) (recomendada) · B - Apenas mascarado · C - Sempre completo
**Escolha:** A - Mascarado com botão "mostrar" (Recommended)
**Por quê:** A opção A está marcada como recomendada, então a política manda escolhê-la. Ela preserva a privacidade por padrão na tela do celular em loja e ainda permite conferir o CPF completo sob demanda.
_claude-code / claude-sonnet-5-5_

## specify · 2026-10-26 03:39
**Pergunta:** Q2 (FR-016): A lista de pedidos deve ter um seletor de "mês de cota" (26 a 25) além do período livre?
**Opções:** A - Só período livre (Recommended) (recomendada) · B - Período livre + atalho de mês de cota · C - Período livre + atalhos simples
**Escolha:** A - Só período livre (Recommended)
**Por quê:** A opção A está marcada como recomendada, então a política manda escolhê-la. Também é a que menos amplia escopo e risco, já que evita implementar a regra de cota 26→25 no frontend. O mês de cota fica para uma HU futura com dashboards.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-26 03:42
**Pergunta:** Ao abrir a lista de pedidos sem nenhum filtro, qual período deve ser exibido por padrão?
**Opções:** Todos os pedidos (Recommended) (recomendada) · Mês calendário atual · Últimos 30 dias
**Escolha:** Todos os pedidos (Recommended)
**Por quê:** É a opção marcada como recomendada, então segue a política definida. Também é a que menos amplia o escopo, pois não adiciona regra de período padrão, e é coerente com a decisão anterior de oferecer apenas período livre, sem mês de cota.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-26 03:43
**Pergunta:** Como a lista de pedidos dentro do perfil do cliente deve ser paginada?
**Opções:** 20 por página (Recommended) (recomendada) · Últimos N + 'ver todos' · Todos de uma vez
**Escolha:** 20 por página (Recommended)
**Por quê:** É a opção marcada como recomendada, então a política manda escolhê-la. Mantém o mesmo padrão e os mesmos controles das listas de clientes e pedidos, sem ampliar o escopo.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-26 03:44
**Pergunta:** Como a vendedora deve escolher o canal no filtro de pedidos?
**Opções:** Campo de texto (Recommended) (recomendada) · Lista fixa de canais · Lista dinâmica
**Escolha:** Campo de texto (Recommended)
**Por quê:** É a opção marcada como recomendada. Também é a que menos amplia escopo e risco, pois não depende de lista prévia, de manutenção no frontend nem de apoio adicional da API.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-26 03:46
**Pergunta:** Em quais navegadores o site precisa funcionar de forma garantida e testada?
**Opções:** Mobile + desktop atuais (Recommended) (recomendada) · Só Chrome (Android e desktop) · Inclui navegadores antigos
**Escolha:** Mobile + desktop atuais (Recommended)
**Por quê:** A opção está marcada como recomendada e cobre o uso principal da vendedora no celular (Chrome Android e Safari iOS), além do desktop. Restringir a só Chrome deixaria iPhone sem garantia, e incluir navegadores antigos ampliaria escopo e testes sem necessidade na história.
_claude-code / claude-sonnet-5-5_

## plan · 2026-10-26 03:49
**Pergunta:** Qual stack de testes do frontend? A spec exige validar 360 px, 3 fusos horários, matriz de navegadores (Chrome/Safari/Edge/Firefox) e acessibilidade básica.
**Opções:** Vitest + RTL + Playwright (Recommended) (recomendada) · Só Vitest + RTL · Vitest + Cypress
**Escolha:** Vitest + RTL + Playwright (Recommended)
**Por quê:** É a opção marcada como recomendada e a única que cobre os requisitos da spec (360 px, fusos horários, matriz de navegadores com WebKit real e acessibilidade com axe-core). As alternativas deixariam critérios de sucesso para verificação manual ou sem Safari real.
_claude-code / claude-sonnet-5-5_

## plan · 2026-10-26 03:49
**Pergunta:** Como obter o ID token do Google no navegador para enviar a POST /auth/google?
**Opções:** Google Identity Services direto (Recommended) (recomendada) · @react-oauth/google
**Escolha:** Google Identity Services direto (Recommended)
**Por quê:** É a opção marcada como recomendada e a que a HU 002 propõe. Não adiciona dependência de runtime extra ao bundle, o que mantém o escopo e o risco menores.
_claude-code / claude-sonnet-5-5_

## plan · 2026-10-26 03:49
**Pergunta:** A API filtra canal por IGUALDADE (OrderRepository.cs:90), mas a spec (FR-014) pede busca por TRECHO, sem distinguir maiúsculas. Como tratar?
**Opções:** Ajustar backend nesta feature (Recommended) (recomendada) · Manter igualdade e documentar
**Escolha:** Ajustar backend nesta feature (Recommended)
**Por quê:** A opção está marcada como recomendada, e a política manda escolhê-la. Ela mantém o frontend fiel ao FR-014 (busca por trecho, sem distinção de maiúsculas/minúsculas) com uma mudança pequena e isolada no OrderRepository, junto com a atualização do openapi.yaml e dos testes.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-26 04:01
**Pergunta:** A análise encontrou 21 problemas, 2 deles CRITICAL. Os dois vêm de pontos em que o plano se afasta do que a constituição exige. Quer que eu sugira edições concretas (sem aplicá-las) para os principais?
**Opções:** Top 7 (Recommended) (recomendada) · Só CRITICAL e HIGH · Todos os 21 · Nenhuma agora
**Escolha:** Top 7 (Recommended)
**Por quê:** A opção está marcada como recomendada, então a política manda escolhê-la. Ela cobre os 2 CRITICAL e os 2 HIGH, além de 3 MEDIUM relevantes, sem ampliar o escopo para todos os 21 problemas. Além disso, apenas sugere edições, sem aplicá-las.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-26 04:04
**Pergunta:** Quer que eu sugira edições concretas (sem aplicá-las) para os problemas que sobraram?
**Opções:** Top 6 (Recommended) (recomendada) · Só HIGH · Todos os 21 · Nenhuma agora
**Escolha:** Top 6 (Recommended)
**Por quê:** A opção está marcada como recomendada, então a política manda escolhê-la. Ela cobre os 2 problemas HIGH e os MEDIUM que afetam a implementação, sem ampliar o escopo para os LOW. Apenas sugere edições, sem aplicá-las.
_claude-code / claude-sonnet-5-5_


## implement · 2026-10-26
**Pergunta:** O limite de `busca`/`search` divergia entre os documentos: `tasks.md` (T064/T072) e `data-model.md` §4 diziam 200 caracteres, mas a API valida `Search` com `MaximumLength(60)` (`OrderListQueryValidator`). O que prevalece?
**Opções:** A - Manter 60 no código e acertar a documentação (Recommended) · B - Aceitar 200 no campo e cortar antes da API · C - Só sinalizar a divergência
**Escolha:** A - Manter 60 no código e acertar a documentação
**Por quê:** Enviar 200 devolveria 400 da API e a vendedora veria um erro em vez da lista — exatamente o que o parser de filtros evita ao descartar inválidos. O código já estava em 60 (`SEARCH_MAX`, `maxLength` do input e os testes de `lib/filters.test.ts`); corrigiram-se `data-model.md` §4 e as descrições de T064/T072.
_maestro / implement_

## implement · 2026-10-26
**Pergunta:** A verificação da SC-008 (T091) falhou: `dev-login` continuava no bundle de produção mesmo com `VITE_DEV_LOGIN` ausente. Colocar o `loginDev` do `authService` atrás de uma guarda de build?
**Opções:** A - Corrigir o import estático de `DevLoginForm` em `LoginPage.tsx` (Recommended) · B - Adicionar guarda de runtime em `authService.loginDev`
**Escolha:** A - Corrigir o import estático
**Por quê:** A causa era um `import DevLoginForm from ./DevLoginForm` usado apenas dentro do `Suspense`: o binding é marcado como utilizado e impedia a eliminação do ramo morto, então o chunk sobrevivia à flag. A guarda de runtime (opção B) bastaria para a busca por texto, mas quebraria o dev-login em Homolog, onde `VITE_DEV_LOGIN=true` e o Rollup não gera chunk estático. `npm run verify:prod` cobre os dois caminhos.
_maestro / implement_

## implement · 2026-10-26
**Pergunta:** O retry de erro (T086) devia viver nas páginas ou nos contextos?
**Opções:** A - No contexto, com a última consulta guardada em ref (Recommended) · B - Nas páginas, recompondo os parâmetros do efeito
**Escolha:** A - No contexto, com a última consulta guardada em ref
**Por quê:** Com a guarda no contexto (`retryCustomers`/`retryCustomer`/`retryOrders`/`retryOrder`), o `onRetry` tem identidade estável: o toast com "Tentar de novo" repete exatamente a consulta que falhou sem a página remontar o efeito de carga e sem reescrever a URL. A opção B obrigava cada página a reconstruir os parâmetros e reinstalava o efeito a cada toque.
_maestro / implement_
