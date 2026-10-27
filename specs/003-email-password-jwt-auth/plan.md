# Implementation Plan: Login com e-mail e senha e criação de conta

**Branch**: `003-email-password-jwt-auth` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/003-email-password-jwt-auth/spec.md`

## Summary

Substituir a entrada de desenvolvimento (`POST /auth/dev-login`) por um cadastro real com senha. O backend ganha `POST /auth/register` (cria a conta e já devolve o `LoginResult`) e `POST /auth/login` (e-mail + senha → mesmo JWT HS256 de 8 h do login Google), com a senha guardada como PBKDF2-HMAC-SHA256 na coluna `users.password_hash` que já existe — **sem migração**. O login Google permanece e, ao reutilizar uma conta que tinha senha, remove a senha (FR-009). A rota `/auth/dev-login`, seu DTO, validator, filtro do Swagger, flag `VITE_DEV_LOGIN`, formulário `DevLoginForm`, requisição do Bruno e menções na documentação são removidos de **todos** os ambientes. O frontend ganha o formulário de e-mail/senha na tela de entrada e a tela pública `/cadastro`; a suíte `Ambrosio.ApiTests` passa a criar os usuários A e B por cadastro + login; a coleção do Bruno ganha "Criar conta" e "Login com senha". A constituição §VI recebe uma emenda (proposta em [constitution-amendment.md](./constitution-amendment.md)).

Decisões técnicas detalhadas em [research.md](./research.md): hash PBKDF2 nativo do .NET (sem dependência nova), resposta `401 + ErrorResult` no login inválido com ajuste do `apiClient` para não confundir com sessão expirada, `409 + ErrorResult` no e-mail duplicado, `201` no cadastro, e-mails únicos por execução nos testes e `verify:prod` rodando em todos os modos de build.

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 (frontend)

**Primary Dependencies**:
- Backend: ASP.NET Core 8, EF Core 9.x + Npgsql, FluentValidation 11, System.IdentityModel.Tokens.Jwt 8, Google.Apis.Auth (login Google mantido), Swashbuckle 8. Hash de senha com `System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2` (BCL — **nenhum pacote novo**)
- Frontend: React Router 6, Vite 6, Tailwind CSS 4 + shadcn/ui (`input`, `label`, `button`, `card` já gerados), i18next 25, sonner; Fetch API via `apiClient` existente; Google Identity Services mantido
- Testes de API: xUnit + Flurl.Http + FluentAssertions (`Ambrosio.ApiTests`, skill `dotnet-test-api`)
- Frontend: Vitest + Testing Library + jsdom

**Storage**: PostgreSQL (tabela `users`, coluna `password_hash varchar(260)` já existente; sem migração). Frontend: `localStorage` na chave `login-with-metamask:auth` com `{ token, expiresAt, user }` (inalterado)

**Testing**: `Ambrosio.ApiTests` contra Development (`http://localhost:5000`) e Homolog (`http://localhost:8080/api`) — cadastro, login, ausência do dev-login, isolamento A/B; Vitest + RTL para `LoginPage`, `RegisterPage`, `AuthContext`, `authService`, `apiClient` e validação de formulário; `npm run verify:prod` em todos os modos de build

**Target Platform**: API Linux (container, atrás do gateway nginx em `/api`); SPA em navegadores móveis e desktop (≥ 360 px)

**Project Type**: web application (`backend/` + `frontend/`), com coleção Bruno e documentação

**Performance Goals**: `POST /auth/login` e `/auth/register` ≤ 500 ms p95 em Homolog, incluindo o custo do PBKDF2 (~150–300 ms por verificação, deliberado); tempo de resposta do login inválido equivalente ao do login válido (SC-006); bundle inicial do frontend continua ≤ 250 kB gzip

**Constraints**:
- Senha nunca em texto puro no banco, em log, em DTO ou em tela (FR-005, constituição §VI)
- Mensagem genérica única para e-mail inexistente / senha errada / conta só-Google, com tempo uniforme (FR-008)
- `/auth/dev-login` responde 404 em todos os ambientes e não aparece no Swagger (FR-012); nenhuma ocorrência de `dev-login`, `DevLoginForm` ou `VITE_DEV_LOGIN` no `dist/` em nenhum modo (FR-013)
- `[AllowAnonymous]` só em `/auth/*`; nenhum `userId` em DTO de entrada; segredo JWT e demais chaves continuam vindo do ambiente
- Sem rate limiting / verificação de e-mail / recuperação de senha (fora do escopo, ver Assumptions da spec)
- Toda string visível do frontend em `src/i18n/locales/pt-BR.ts`; casing de pastas da constituição §III

**Scale/Scope**: 2 endpoints novos + 1 removido; 2 DTOs de entrada + 2 validators + 1 AppService novo; 2 telas do frontend (entrada reformulada + cadastro novo) e 1 componente (`PasswordInput`); ~10 testes de API novos; 2 requisições Bruno novas + 1 removida; ~8 arquivos de documentação ajustados; 1 emenda constitucional

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Princípio | Como o plano atende | Status |
|---|---|---|---|
| I | Skills de arquitetura obrigatórias | Backend pela `dotnet-architecture`: DTOs `RegisterInfo`/`LoginInfo` em `Ambrosio.DTO/Auth`, validators FluentValidation no Domain (`dotnet-fluent-validation`), `IPasswordHashAppService` em `Infra.Interfaces/AppServices` + `PasswordHashAppService` em `Infra/AppServices`, regras em `AuthService`, registro no `Startup.cs`. Frontend pela `react-architecture`: `types/auth.ts` → `Services/authService.ts` → `Contexts/AuthContext.tsx` → `hooks/useAuth.ts` (provider já registrado em `main.tsx`); erros com `handleError`/`clearError`/`loading`. Testes de API pela `dotnet-test-api`. Compose/Dockerfile pela `dotnet-env`; Docker local pela `wsl-docker`. `dotnet-graphql` não se aplica (REST). A exceção formal da regra 7 da skill `react-architecture` (DTOs puros em 200) registrada no plano 002 permanece e vale para os novos serviços. | ✅ (exceção herdada da 002) |
| II | Stack fixa | Nenhuma dependência nova: PBKDF2 da BCL (`Rfc2898DeriveBytes.Pbkdf2`), EF Core, FluentValidation, Fetch API, Tailwind + shadcn/ui, i18next, Vitest. Sem Identity, sem BCrypt, sem Axios, sem Playwright. Variáveis `VITE_*` (a `VITE_DEV_LOGIN` é removida). | ✅ |
| III | Case sensitivity | Arquivos novos em `src/Services/`, `src/Contexts/`, `src/hooks/`, `src/types/`, `src/pages/`, `src/components/common/`; imports com casing exato (o build roda em Linux no Docker). | ✅ |
| IV | Convenções de código | Backend: `RegisterInfo`/`LoginInfo` (nomeação `Info`), `[JsonPropertyName("camelCase")]`, `ErrorResult` com `sucesso`/`mensagem`/`erros`, namespaces file-scoped, constantes `UPPER_CASE` (`PBKDF2_ITERATIONS`). Frontend: componentes PascalCase (`RegisterPage`, `PasswordInput`), arrow functions, `interface` (nunca `type`), acesso direto em camelCase. | ✅ |
| V | Banco de dados | Nenhuma tabela, coluna, índice ou migração: `password_hash varchar(260)` já existe e comporta o formato PBKDF2 (~100 caracteres); `ux_users_email` já garante unicidade e resolve a corrida do cadastro simultâneo. `provider` continua `integer` (valor 2 passa de `Dev` a `Password`). | ✅ (N/A) |
| VI | Autenticação e segurança | JWT HS256 emitido pela própria API; mesmo `JwtTokenAppService`, mesmos claims, mesma expiração. Senha só em hash salgado (salt aleatório por senha); `PasswordHash` nunca mapeado para DTO (`UserInfo` inalterado). `[AllowAnonymous]` continua restrito a `/auth/*`. `/auth/dev-login` removido em todos os ambientes — **mais restritivo** que o texto atual da §VI (que o *permite* em Development/Homolog), portanto não é violação; a §VI descreve o esquema como "após login OAuth/OIDC (Google)" e precisa ser **emendada** para incluir cadastro/login com senha e proibir o dev-login. Proposta em [constitution-amendment.md](./constitution-amendment.md), a ser aprovada no PR deste ciclo. | ⚠️ Emenda proposta (sem violação) |
| VII | Ambientes e testes de API | Endpoints novos/alterados ganham testes em `Ambrosio.ApiTests` no mesmo ciclo; a homologação é reconstruída (`docker compose --profile frontend up -d --build` via WSL) e a suíte roda contra `http://localhost:8080/api` sem usuário pré-existente (SC-005). Falhas são corrigidas no código, nunca desabilitando testes. Alterações de compose/Dockerfile (remoção do arg `VITE_DEV_LOGIN`) seguem a `dotnet-env`. | ✅ |
| VIII | Estratégia de testes | Backend coberto pelos testes de API; frontend só com Vitest + RTL (nenhum E2E). O projeto `Ambrosio.Tests` (unitários) já existe antes desta feature e é um desvio pré-existente da §VIII. Esta feature **reduz** o desvio em vez de mantê-lo: os testes unitários tocados pela mudança de autenticação (`AuthServiceTest.cs` e `DevLoginInfoValidatorTest.cs` inteiros, e os casos de `UserServiceTest.cs` que referenciam `AuthProvider`) são **apagados**, nunca ajustados (T003); o comportamento do `AuthService` passa a ser coberto só pelos testes de API (T016, T031, T049, T051). **Nenhum teste unitário do backend é criado, ajustado ou executado** nesta feature. A remoção completa do projeto `Ambrosio.Tests` fica registrada como follow-up em `decisions.md` (T056). | ✅ (desvio pré-existente reduzido, não ampliado) |
| — | Variáveis de ambiente | Backend: nenhuma variável nova (PBKDF2 usa salt por senha, não o pepper do CPF). Frontend: `VITE_API_URL`, `VITE_SITE_BASENAME`, `VITE_GOOGLE_CLIENT_ID` permanecem; `VITE_DEV_LOGIN` é apagada de `.env.*`, `vite-env.d.ts`, `Dockerfile` e dos dois `docker-compose`. | ✅ |
| — | Tratamento de erros | Controllers com `try/catch` → `ValidationError` (400), `ConflictError` (409), `Unauthorized` + `ErrorResult` (401 do login), `StatusCode(500, ex.Message)`. Frontend: `apiClient` normaliza (com o ajuste do research §3 para 401 anônimo) e o `AuthContext` expõe `error`/`clearError`/`loading`. | ✅ |

**Resultado do gate (pré-Phase 0)**: APROVADO. Nenhuma violação; uma emenda constitucional (§VI) proposta como parte do ciclo, conforme a spec exige.

**Re-avaliação pós-Phase 1**: data-model (sem migração, senha nunca em DTO), contratos (`/auth/register`, `/auth/login`, `/auth/google` reutilizando `LoginResult`/`ErrorResult`; `/auth/dev-login` removido) e quickstart (homolog reconstruída + ApiTests + `verify:prod` em todos os modos) mantêm todas as regras acima. APROVADO.

## Project Structure

### Documentation (this feature)

```text
specs/003-email-password-jwt-auth/
├── plan.md                      # Este arquivo
├── spec.md                      # Especificação funcional
├── research.md                  # Phase 0: decisões técnicas
├── data-model.md                # Phase 1: User, sessão, DTOs, regras e transições
├── quickstart.md                # Phase 1: guia de validação ponta a ponta
├── constitution-amendment.md    # Proposta de emenda da §VI (governança)
├── contracts/
│   ├── auth-api.md              # Delta OpenAPI: /auth/register, /auth/login, /auth/google, remoção do dev-login
│   └── ui-routes.md             # Rotas /login e /cadastro, estados, returnTo, i18n
├── checklists/requirements.md
├── decisions.md
└── tasks.md                     # Phase 2 (gerado por /speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── Ambrosio.DTO/
│   ├── Auth/RegisterInfo.cs                 # NOVO: name, email, password
│   ├── Auth/LoginInfo.cs                    # NOVO: email, password
│   ├── Auth/DevLoginInfo.cs                 # REMOVIDO
│   ├── Auth/LoginResult.cs                  # inalterado (resposta de register/login/google)
│   └── Enums/AuthProvider.cs                # Dev = 2 → Password = 2 (JSON "password")
├── Ambrosio.Domain/
│   ├── Models/User.cs                       # + SetPassword/ClearPassword, HasPassword
│   ├── Interfaces/IAuthService.cs           # RegisterAsync, LoginWithPasswordAsync; DevLoginAsync removido
│   ├── Services/AuthService.cs              # cadastro, login com senha, Google remove senha
│   ├── Exceptions/InvalidCredentialsException.cs   # NOVO → 401 genérico
│   ├── Validators/RegisterInfoValidator.cs  # NOVO
│   ├── Validators/LoginInfoValidator.cs     # NOVO
│   └── Validators/DevLoginInfoValidator.cs  # REMOVIDO
├── Ambrosio.Infra.Interfaces/AppServices/IPasswordHashAppService.cs   # NOVO
├── Ambrosio.Infra/AppServices/PasswordHashAppService.cs               # NOVO (PBKDF2-HMAC-SHA256)
├── Ambrosio.Application/Startup.cs          # registra IPasswordHashAppService (singleton)
├── Ambrosio.API/
│   ├── Controllers/AuthController.cs        # Register (201), Login (200/401), Google; DevLogin removido
│   ├── Swagger/DevLoginDocumentFilter.cs    # REMOVIDO
│   ├── Extensions/HostEnvironmentExtensions.cs  # IsDevLoginEnabled removido (arquivo some se a constante Homolog ficar sem uso)
│   └── Program.cs                           # sem DocumentFilter; descrição do Bearer atualizada
├── Ambrosio.ApiTests/
│   ├── Fixtures/ApiTestFixture.cs           # RegisterAsync + LoginAsync + CreateUserAsync (cadastro → login)
│   ├── Controllers/AuthControllerTests.cs   # cadastro/login/404 do dev-login
│   ├── Controllers/UnauthorizedAccessTests.cs  # rota pública = /auth/login
│   └── appsettings.Test.json                # Auth:TestPassword (não secreto)
├── Ambrosio.Tests/                          # testes de auth APAGADOS (AuthServiceTest, DevLoginInfoValidatorTest, casos de AuthProvider em UserServiceTest); nada ajustado nem criado
└── README.md, scripts/seed-load-test.sql    # docs sem dev-login

frontend/
├── .env.example / .env.development / .env.homolog / .env.production   # sem VITE_DEV_LOGIN
├── Dockerfile                               # sem ARG/ENV VITE_DEV_LOGIN; verify-prod em TODOS os modos
├── scripts/verify-prod.mjs                  # FORBIDDEN += 'VITE_DEV_LOGIN'
├── package.json                             # (inalterado: verify:prod)
└── src/
    ├── vite-env.d.ts                        # sem VITE_DEV_LOGIN
    ├── App.tsx                              # + rota pública /cadastro
    ├── i18n/locales/pt-BR.ts                # login.*, register.*, validation.*, errors.* (sem devLoginUnavailable)
    ├── types/auth.ts                        # RegisterInfo, LoginInfo; provider 'google' | 'password'; sem DevLoginInfo
    ├── lib/authForm.ts (+ .test.ts)         # validação pura dos formulários (nome, e-mail, senha, confirmação)
    ├── Services/apiClient.ts (+ .test.ts)   # 401 em chamada anônima → ApiError (não dispara logout)
    ├── Services/authService.ts              # register, loginWithPassword, loginWithGoogle; sem loginDev
    ├── Contexts/AuthContext.tsx (+ .test.tsx)  # register/loginWithPassword no lugar de loginDev
    ├── components/common/PasswordInput.tsx (+ .test.tsx)  # campo com mostrar/ocultar (FR-020)
    ├── pages/LoginPage.tsx (+ .test.tsx)    # formulário e-mail/senha + link "Criar conta" + Google
    ├── pages/RegisterPage.tsx (+ .test.tsx) # NOVO: /cadastro
    └── pages/DevLoginForm.tsx               # REMOVIDO

bruno/
├── Auth/Criar conta.bru                     # NOVO (seq 1) — grava token
├── Auth/Login com senha.bru                 # NOVO (seq 2) — grava token
├── Auth/Google Login.bru                    # seq 3
├── Auth/Dev Login.bru                       # REMOVIDO
├── collection.bru / environments/Homolog.bru   # userEmail, userName (vars) + userPassword, token (secret)

docker-compose.yml / docker-compose-prod.yml # sem build.arg VITE_DEV_LOGIN
deploy/README.md, README.md, docs/README.md, docs/historias/*.md, MAESTRO.md   # sem dev-login (FR-015)
specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml   # contrato consolidado: + register/login, − dev-login
.specify/memory/constitution.md              # emenda §VI (após aprovação)
```

**Structure Decision**: aplicação web existente (`backend/` Clean Architecture + `frontend/` Vite), sem projetos novos. O hash de senha é um **AppService da Infra** (preocupação criptográfica, como `CpfProtectionAppService`), consumido pelo `AuthService` do Domain via interface — assim o Domain não depende de `System.Security.Cryptography` diretamente e o algoritmo pode ser trocado sem tocar nas regras. A falha de credencial é uma exceção de domínio própria (`InvalidCredentialsException`) mapeada para 401 no controller, mantendo o padrão "exceção → HTTP" do `ApiControllerBase`. No frontend, a tela de cadastro é uma rota própria (`/cadastro`, decisão da clarificação) e a validação dos formulários fica em funções puras em `lib/authForm.ts`, testáveis em isolamento como `lib/cpf.ts`.

## Complexity Tracking

> Preenchido apenas para desvios justificados.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Exceção formal herdada (§I, regra 7 da skill `react-architecture`): `authService.register`/`loginWithPassword` devolvem `LoginResult` puro em 2xx, sem checar `result.sucesso` | Mesmo contrato da HU 001/002: DTOs puros em sucesso e `ErrorResult` só em erro; o `apiClient` já converte erros em classes | Introduzir wrapper `{ sucesso, dados }` só nos endpoints novos criaria dois contratos de resposta na mesma API |
| Ajuste no `apiClient` (401 em chamada anônima vira `ApiError` em vez de disparar o handler de sessão expirada) | Sem isso, "E-mail ou senha inválidos" (401) faria o `AuthContext` executar `logout('expirada')` e mostrar "Sua sessão expirou" na própria tela de login | Devolver 400 para credencial inválida misturaria erro de autenticação com erro de validação e contrariaria o significado HTTP; o ajuste é de três linhas e coberto por teste unitário |
| Emenda constitucional (§VI) dentro da feature | A spec exige remover o dev-login de todos os ambientes, e a §VI atual ainda o descreve como permitido em Development/Homolog e descreve o esquema como Google-only | Deixar a constituição desatualizada violaria a Governance ("constituição prevalece"); a emenda é um arquivo de proposta revisado no mesmo PR |
