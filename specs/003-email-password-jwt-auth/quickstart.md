# Quickstart — 003-email-password-jwt-auth

Guia de validação ponta a ponta. Referências: [contracts/auth-api.md](./contracts/auth-api.md),
[contracts/ui-routes.md](./contracts/ui-routes.md), [data-model.md](./data-model.md).

## Pré-requisitos

- .NET 8 SDK, Node 22+, PostgreSQL local (Development) e Docker Engine no WSL2 (Homolog, skill `wsl-docker`).
- `.env` na raiz preenchido a partir de `.env.example` (segredos JWT/CPF). **Nenhuma variável nova** é exigida por esta feature; `VITE_DEV_LOGIN` deixa de existir.
- Bruno CLI (`bru`) opcional para o cenário 6.

## Cenário 1 — API em Development: cadastro, login e ausência do dev-login

```powershell
cd backend
dotnet build
.\scripts\run-api.ps1            # http://localhost:5000
```

```powershell
# Cadastro → 201 com token e provider "password"
curl -s -X POST http://localhost:5000/auth/register -H "Content-Type: application/json" `
  -d '{"name":"Ana","email":"Ana@Exemplo.com ","password":"senha-forte-1"}'

# Login (e-mail normalizado) → 200, mesmo user.id
curl -s -X POST http://localhost:5000/auth/login -H "Content-Type: application/json" `
  -d '{"email":"ana@exemplo.com","password":"senha-forte-1"}'

# Senha errada e e-mail inexistente → ambos 401 com a MESMA mensagem
curl -s -o NUL -w "%{http_code}\n" -X POST http://localhost:5000/auth/login -H "Content-Type: application/json" -d '{"email":"ana@exemplo.com","password":"errada-123"}'
curl -s -o NUL -w "%{http_code}\n" -X POST http://localhost:5000/auth/login -H "Content-Type: application/json" -d '{"email":"ninguem@exemplo.com","password":"errada-123"}'

# Cadastro repetido → 409 "Este e-mail já está cadastrado."
# Senha curta → 400 com erros[].campo == "password"
# Dev-login → 404 vazio, igual a /auth/qualquer-coisa
curl -s -o NUL -w "%{http_code}\n" -X POST http://localhost:5000/auth/dev-login -H "Content-Type: application/json" -d '{"email":"x@x.com","name":"X"}'
```

Esperado: 201 · 200 · 401 · 401 · 409 · 400 · 404. Em `http://localhost:5000/swagger` as
operações `POST /auth/register` e `POST /auth/login` aparecem e `/auth/dev-login` não existe
(SC-003). O corpo das respostas nunca contém `password`/`passwordHash` (SC-007).

## Cenário 2 — Testes de API contra Development

```powershell
cd backend
dotnet test Ambrosio.ApiTests            # ApiBaseUrl padrão: http://localhost:5000
```

Esperado: 100% verde, sem nenhum usuário criado manualmente; os logs do fixture mostram
`POST /auth/register` (201) seguido de `POST /auth/login` (200) para A e B. Rodar **duas vezes
seguidas**: a segunda também passa (e-mails com GUID — FR-016, SC-005).

## Cenário 3 — Frontend: testes unitários, build e prova do pacote em todos os modos

```powershell
cd frontend
npm ci
npm run lint
npm run type-check
npm test                                  # LoginPage, RegisterPage, AuthContext, authForm, PasswordInput, apiClient
npm run build -- --mode development; npm run verify:prod
npm run build -- --mode homolog;     npm run verify:prod
npm run build -- --mode production;  npm run verify:prod
```

Esperado: `verify:prod` imprime `OK — nenhuma ocorrência de "dev-login" / "DevLoginForm" /
"VITE_DEV_LOGIN"` nos **três** modos (SC-004) e o entry JS continua dentro do orçamento.
`git grep -i "dev-login\|VITE_DEV_LOGIN\|DevLogin" -- frontend/src frontend/.env*` não
retorna nada.

## Cenário 4 — Frontend manual (dev server)

```powershell
cd frontend
npm run dev                               # http://localhost:5173, proxy /api → :5000
```

1. Abrir `http://localhost:5173/pedidos` sem sessão → redirecionado a `/login`.
2. Clicar **Criar conta** → `/cadastro`. Enviar com senha de 5 caracteres → mensagem no campo, nada enviado. Confirmação diferente → "As senhas não conferem".
3. Preencher nome, e-mail novo e senha válida → vai direto para **`/pedidos`** (returnTo preservado, US1 cenário 7) com nome/e-mail no cabeçalho.
4. **Sair** → `/login`; entrar com senha errada → "E-mail ou senha inválidos" **na própria tela** (não "Sua sessão expirou") e a senha continua no campo.
5. Entrar com a senha certa → `/clientes`. Fechar e reabrir a aba → continua conectada (FR-010).
6. Abrir `/cadastro` já conectada → redirecionada a `/clientes`.
7. Tentar criar conta com o e-mail já usado → "Este e-mail já está cadastrado."
8. Reduzir a janela a 360 px: sem rolagem horizontal; `Tab` percorre todos os campos com foco visível; o botão Mostrar/Ocultar alterna o campo de senha (FR-019/FR-020).
9. Com `VITE_GOOGLE_CLIENT_ID` preenchido, o botão Google aparece acima do formulário e funciona como antes (SC-009); vazio, a tela mostra só o formulário.

## Cenário 5 — Homologação reconstruída + testes de API (encerramento do ciclo, constituição §VII)

```powershell
wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build
```

O build do frontend em homolog executa `verify-prod.mjs` dentro do Dockerfile (falha o build se
algum termo proibido sobreviver). Depois:

```powershell
cd backend
$env:ApiBaseUrl = "http://localhost:8080/api"
dotnet test Ambrosio.ApiTests
Remove-Item Env:ApiBaseUrl
```

Esperado: 100% verde em duas execuções consecutivas (SC-005). `http://localhost:8080/` abre o
app com o formulário de e-mail/senha e sem formulário de desenvolvimento;
`http://localhost:8080/api/swagger` não lista `/auth/dev-login`;
`curl -o NUL -w "%{http_code}" -X POST http://localhost:8080/api/auth/dev-login` → 404.

## Cenário 6 — Coleção do Bruno contra Homolog

```powershell
cd bruno
bru run --env Homolog
```

Esperado: **Criar conta** responde 201 na primeira execução (grava `token`) e 409 nas
seguintes (sem gravar); **Login com senha** responde 200 e grava `token`; todas as demais
requisições (Users, Customers, Orders) passam com esse token; não existe mais "Dev Login"
e **Google Login** permanece (SC-008). Para a primeira execução em um banco novo, preencher
`userPassword` no ambiente Homolog (variável secreta).

## Cenário 7 — Login Google em conta com senha (FR-009, manual)

Exige `AUTH_GOOGLE_CLIENT_ID`/`VITE_GOOGLE_CLIENT_ID` configurados e uma conta Google real.

1. Criar conta por senha com o e-mail da conta Google.
2. Sair e entrar com **Google** → mesma conta (mesmo `id` em `/users/me`), `provider` passa a `google`.
3. Sair e tentar entrar com a senha anterior → "E-mail ou senha inválidos".
4. Tentar criar conta com esse e-mail → "Este e-mail já está cadastrado. Entre com sua conta Google."

## Cenário 8 — Documentação e constituição

```powershell
git grep -n -i "dev-login\|devLogin\|VITE_DEV_LOGIN\|devEmail" -- . ":!specs/" ":!backend/.tmp-stage"
```

Esperado: nenhuma ocorrência fora de `specs/` (FR-015). A constituição em
`.specify/memory/constitution.md` está na versão 3.3.0 com a §VI emendada conforme
[constitution-amendment.md](./constitution-amendment.md).
