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
