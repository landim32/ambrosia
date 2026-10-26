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
