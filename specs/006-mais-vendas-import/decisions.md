# Decisões do autopiloto — 006-mais-vendas-import

Execução 20261027-112248 · história 1/1 "004 — Importação do +Vendas pelo frontend (pedidos, clientes e fotos)"

## clarify · 2026-10-27 14:27
**Pergunta:** Em que situação uma execução termina como "parcial", e não como "concluída" ou "falhou"? Hoje a spec lista o status "parcial" (FR-047), mas não diz quando ele se aplica. Isso define o que a tela destaca e o que os testes de aceitação verificam.
**Opções:** Itens com erro (Recommended) (recomendada) · Só pedidos com erro · Blocos com sucesso + falho · Remover "parcial"
**Escolha:** Itens com erro (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela também se alinha à história: o status parcial sinaliza que todos os blocos terminaram, mas houve itens com erro, como CPF inválido, conflito com pedido manual ou imagem com erro. Já 'falhou' fica reservado para bloco falho, como nos critérios 11 e 29.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-27 14:28
**Pergunta:** Se a vendedora editar no Ambrosia um pedido que veio do +Vendas, o que deve acontecer com essa edição quando o período for importado de novo e a origem tiver outro conteúdo? Hoje FR-023 atualiza o pedido com os dados da origem, mas não diz nada sobre edições feitas à mão. Isso afeta o modelo de dados e os testes de idempotência.
**Opções:** Origem prevalece (Recommended) (recomendada) · Bloquear edição · Edição manual prevalece
**Escolha:** Origem prevalece (Recommended)
**Por quê:** A opção 1 está marcada como recomendada e é coerente com a história, que trata a importação como espelho idempotente do +Vendas (upsert por chave natural com substituição de itens e registro dos campos alterados). Os campos que existem só no Ambrosia são preservados, sem exigir rastreamento de edições manuais nem mudar a tela de pedido.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-27 14:28
**Pergunta:** O Ambrosia deve limitar as tentativas seguidas de importação com credenciais recusadas pelo +Vendas? Sem limite, a tela pode virar um jeito de testar senhas contra o +Vendas, e a conta da vendedora pode ser bloqueada lá. Isso define uma regra a mais e um teste de API.
**Opções:** 3 falhas / 15 min (Recommended) (recomendada) · 5 falhas / 1 hora · Sem limite
**Escolha:** 3 falhas / 15 min (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela protege a conta da vendedora no +Vendas e evita que a tela sirva para testar senhas, com uma regra simples e um teste de API.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-27 14:28
**Pergunta:** O que o Ambrosia deve fazer se a vendedora importar usando uma conta do +Vendas diferente da que usou antes (por exemplo, a conta de uma colega)? Os pedidos dessa conta entrariam na base dela, misturando vendas de outra pessoa. Proteger contra isso exige guardar uma identificação da conta, mas nunca o login em si.
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
