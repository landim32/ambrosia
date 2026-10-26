# Decisões do autopiloto — 003-email-password-jwt-auth

Execução 20261026-172724 · história 1/1 "Alterar o login:"

## specify · 2026-10-26 20:30
**Pergunta:** Q1 — Login com Google: deve ser mantido ao lado do login com e-mail e senha, ou removido?
**Opções:** A: Manter Google + senha (Recommended) (recomendada) · B: Remover Google
**Escolha:** A: Manter Google + senha (Recommended)
**Por quê:** A opção A está marcada como recomendada. Além disso, a história só pede remover o /auth/dev-login e adicionar e-mail/senha, sem mencionar a remoção do Google. Manter o Google evita ampliar escopo e risco.
_claude-code / claude-sonnet-5-5_

## specify · 2026-10-26 20:30
**Pergunta:** Q2 — Criar conta com um e-mail que já existe por conta Google (usuário sem senha): o que fazer?
**Opções:** A: Recusar e orientar (Recommended) (recomendada) · B: Vincular senha à conta existente · C: Recusar sem detalhar
**Escolha:** A: Recusar e orientar (Recommended)
**Por quê:** A opção A está marcada como recomendada, então segue a política. Ela também evita o risco de segurança de vincular senha a uma conta Google sem verificar o e-mail, além de não ampliar o escopo.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-26 20:32
**Pergunta:** Quando alguém entra com Google usando um e-mail que já tem conta com senha (criada sem verificação de e-mail, possivelmente por outra pessoa), o que deve acontecer com a senha existente? Por que importa: sem verificação de e-mail, quem cadastrou o e-mail de outra pessoa continuaria entrando com a senha depois que a dona real entrar pelo Google (sequestro de conta pré-criada).
**Opções:** Reutilizar e apagar senha (Recommended) (recomendada) · Reutilizar e manter senha · Recusar o login Google
**Escolha:** Reutilizar e apagar senha (Recommended)
**Por quê:** A opção 1 está marcada como recomendada e fecha o risco de sequestro de conta pré-criada, já que o Google comprova a posse do e-mail. Não amplia o escopo de forma relevante: apenas remove a senha existente ao reutilizar a conta.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-26 20:34
**Pergunta:** Onde fica o formulário de "Criar conta" no frontend? Por que importa: define rotas, redirecionamento pós-cadastro (FR-011), links compartilháveis e os testes unitários da tela de entrada (FR-018).
**Opções:** Rota própria /cadastro (Recommended) (recomendada) · Abas na tela de entrada · Alternar no mesmo card
**Escolha:** Rota própria /cadastro (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Uma rota própria /cadastro também mantém cada tela simples e dá URL direta para o redirecionamento pós-cadastro.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-26 20:49
**Pergunta:** Quer que eu sugira edições concretas para os principais problemas? Nada será aplicado sem a sua aprovação.
**Opções:** Top 5 (Recommended) (recomendada) · Só o crítico · Todos · Nenhum
**Escolha:** Top 5 (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela cobre os principais achados sem ampliar o escopo para todos os 14. Nada é aplicado sem aprovação, então o risco é baixo.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-26 20:56
**Pergunta:** Quer que eu sugira edições concretas para os principais problemas restantes? Nada será aplicado sem a sua aprovação.
**Opções:** Top 5 MEDIUM (Recommended) (recomendada) · Só governança · Todos · Nenhum
**Escolha:** Top 5 MEDIUM (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela também limita as sugestões aos principais achados, sem ampliar o escopo. Nada é aplicado sem aprovação do usuário.
_claude-code / claude-sonnet-5-5_
