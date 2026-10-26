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
