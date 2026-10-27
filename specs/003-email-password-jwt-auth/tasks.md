---

description: "Lista de tarefas — Login com e-mail e senha e criação de conta"
---

# Tasks: Login com e-mail e senha e criação de conta

**Input**: Documentos de design em `specs/003-email-password-jwt-auth/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/auth-api.md, contracts/ui-routes.md, quickstart.md, constitution-amendment.md

**Tests**: SIM. A spec exige testes de API (US4, FR-016, FR-017, constituição §VII) e o research §8 define os testes unitários do frontend (Vitest + RTL). **Nenhum teste unitário novo no backend** (em `Ambrosia.Tests` os testes afetados pela autenticação são **apagados**, nunca ajustados, e o projeto não é executado nesta feature — constituição §VIII, plan.md).

**Organization**: tarefas agrupadas por história de usuário. **Atenção à ordem**: a US4 (P2) é executada **antes** da US3 (P1), porque remover o `/auth/dev-login` (US3) quebra o `ApiTestFixture`, que hoje cria os usuários A/B por dev-login. Migrar a suíte para cadastro + login (US4) primeiro mantém o backend verde a cada checkpoint.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história de usuário da tarefa (US1, US2, US3, US4)
- Caminhos relativos à raiz do repositório (`C:\repos\ambrosia`)

## Path Conventions

- Backend: `backend/Ambrosia.<Camada>/...` (Clean Architecture — skill `dotnet-architecture`)
- Frontend: `frontend/src/...` (casing fixo: `Contexts/`, `Services/` maiúsculos; `hooks/`, `types/` minúsculos)
- Bruno: `bruno/...`; Docker: `docker-compose*.yml`, `frontend/Dockerfile` (skill `dotnet-env`; execução pela skill `wsl-docker`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: confirmar a linha de base antes de mexer na autenticação.

- [X] T001 Registrar a linha de base: rodar `dotnet build` em `backend/` e `npm ci; npm run lint; npm run type-check; npm test` em `frontend/`, anotando em `specs/003-email-password-jwt-auth/decisions.md` (seção "tasks · baseline") qualquer falha pré-existente, para não confundi-la com regressão desta feature
- [X] T002 [P] Gerar o inventário de ocorrências a remover com `git grep -n -i "dev-login\|devLogin\|DevLoginForm\|VITE_DEV_LOGIN\|devEmail\|devName\|AuthProvider.Dev" -- . ":!specs/"` e colar a lista de arquivos em `specs/003-email-password-jwt-auth/decisions.md` (seção "tasks · inventário dev-login"), servindo de checklist para a Phase 6 (US3)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: peças compartilhadas por cadastro (US1) e login (US2): enum, modelo de usuário, hash de senha, exceção 401, ajuste do `apiClient`, tipos, utilitários de formulário, `PasswordInput` e chaves i18n.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase terminar.

### Backend

- [X] T003 [P] Renomear `Dev = 2` para `Password = 2` em `backend/Ambrosia.DTO/Enums/AuthProvider.cs` (valor inteiro inalterado, JSON `"password"`, **sem migração** — research §4/§12), atualizando todas as referências a `AuthProvider.Dev` para compilar: `backend/Ambrosia.Domain/Services/AuthService.cs`, `backend/Ambrosia.Domain/Models/User.cs`, `backend/Ambrosia.ApiTests/Controllers/AuthControllerTests.cs`, `backend/Ambrosia.ApiTests/Controllers/UsersControllerTests.cs`; atualizar o XML doc do membro ("conta criada por `/auth/register`"). No mesmo passo, **apagar** (nunca ajustar) em `backend/Ambrosia.Tests/` os arquivos `Domain/Services/AuthServiceTest.cs` e `Domain/Validators/DevLoginInfoValidatorTest.cs` inteiros e, em `Domain/Services/UserServiceTest.cs`, só os casos que referenciam `AuthProvider` (constituição §VIII); o comportamento do `AuthService` passa a ser coberto pelos testes de API (T016, T031, T049, T051)
- [X] T004 [P] Adicionar ao modelo `backend/Ambrosia.Domain/Models/User.cs`: `bool HasPassword => PasswordHash is not null`; `void SetPassword(string passwordHash, DateTime now)` (define o hash e `UpdatedAt`); `void ClearPassword(DateTime now)` (`PasswordHash = null`, `UpdatedAt = now`); `void Touch(DateTime now)` (`UpdatedAt = now`). Invariante: `PasswordHash` **nunca** é mapeado para `UserInfo` nem qualquer DTO (FR-005); coluna `password_hash varchar(260) NULL` já existe — não criar migração
- [X] T005 [P] Criar `backend/Ambrosia.Domain/Exceptions/InvalidCredentialsException.cs` (namespace file-scoped, segue o padrão de `DuplicateException.cs`) com mensagem fixa `"E-mail ou senha inválidos"` exposta como constante `MESSAGE`, para o 401 genérico do FR-008
- [X] T006 [P] Criar `backend/Ambrosia.Infra.Interfaces/AppServices/IPasswordHashAppService.cs` com `string Hash(string password)`, `bool Verify(string password, string? passwordHash)` e `string DummyHash { get; }`, com XML docs em português (data-model §7)
- [X] T007 Implementar `backend/Ambrosia.Infra/AppServices/PasswordHashAppService.cs` (depende de T006): PBKDF2-HMAC-SHA256 via `Rfc2898DeriveBytes.Pbkdf2`, constantes `PBKDF2_ITERATIONS = 600000`, `SALT_SIZE = 16`, `HASH_SIZE = 32`, salt de `RandomNumberGenerator`, formato `pbkdf2-sha256$600000$<salt base64>$<hash base64>` (~90 caracteres, cabe em `varchar(260)`); `Verify` devolve `false` para hash nulo/malformado/algoritmo desconhecido, lê as iterações do próprio hash e compara com `CryptographicOperations.FixedTimeEquals`; `DummyHash` = hash de uma senha aleatória calculado uma vez no construtor (research §1/§2). Nenhum pacote NuGet novo
- [X] T008 Registrar `IPasswordHashAppService` → `PasswordHashAppService` como **singleton** em `backend/Ambrosia.Application/Startup.cs` (depende de T007), junto dos demais AppServices (`CpfProtectionAppService`, `JwtTokenAppService`)
- [X] T009 [P] Adicionar a `backend/Ambrosia.API/Controllers/ApiControllerBase.cs` o helper `protected IActionResult InvalidCredentialsError(InvalidCredentialsException ex)` que devolve `401` com `ErrorResult { Sucesso = false, Mensagem = ex.Message, Erros = [] }` (contracts/auth-api.md), com XML doc citando FR-008

### Frontend

- [X] T010 [P] Em `frontend/src/types/auth.ts`: `UserInfo.provider: 'google' | 'password'` (antes `'dev'`), adicionar `export interface RegisterInfo { name: string; email: string; password: string }` e `export interface LoginInfo { email: string; password: string }` (sempre `interface`, nunca `type`; `DevLoginInfo` só é removido na US3)
- [X] T011 [P] Escrever primeiro o caso em `frontend/src/Services/apiClient.test.ts`: resposta 401 em chamada **anônima** (`withAuth === false`) lança `ApiError(401, mensagem do ErrorResult)` e **não** chama o handler de sessão expirada; 401 em chamada autenticada continua lançando `UnauthorizedError` e chamando o handler (deve falhar antes de T012)
- [X] T012 Ajustar `frontend/src/Services/apiClient.ts` (depende de T011): quando `withAuth === false` e o status é 401, ler o `ErrorResult` e lançar `ApiError(401, mensagem, erros)` sem disparar o handler de 401 (research §3); comportamento das chamadas autenticadas inalterado
- [X] T013 [P] Criar `frontend/src/lib/authForm.ts` com as funções compartilhadas: `returnToOf(state: unknown): string | undefined` **movida** de `frontend/src/pages/LoginPage.tsx` (atualizar o import em `LoginPage.tsx`) e `fieldErrorsOf(err: unknown): Partial<Record<'name' | 'email' | 'password', string>>` que mapeia `ApiError.erros[].campo` → mensagem; criar `frontend/src/lib/authForm.test.ts` cobrindo as duas funções (estado sem `returnTo`, `returnTo` não-string, erros com campo desconhecido ignorados)
- [X] T014 [P] Criar `frontend/src/components/common/PasswordInput.tsx` (arrow function, PascalCase, `interface PasswordInputProps`): `Input` do shadcn com botão `type="button"` que alterna `type` entre `password` e `text`, `aria-pressed`, `aria-label` vindo de `toggleLabels: { show: string; hide: string }`, `min-h-11` no input e no botão, repassando `id`, `name`, `value`, `onChange`, `autoComplete`, `aria-describedby`, `aria-invalid` (FR-020, contracts/ui-routes.md); e `frontend/src/components/common/PasswordInput.test.tsx` (inicia oculto, alterna para texto e volta, `aria-pressed` acompanha, não submete o formulário)
- [X] T015 [P] Adicionar as chaves novas em `frontend/src/i18n/locales/pt-BR.ts` com os textos exatos de contracts/ui-routes.md: `login.password` "Senha", `login.or` "ou", `login.noAccount` "Não tem conta?", `login.createAccount` "Criar conta", `register.title` "Criar conta", `register.subtitle` "Use seu e-mail e uma senha para acessar o Ambrosia.", `register.name` "Nome", `register.confirmPassword` "Confirmar senha", `register.submit` "Criar conta", `register.haveAccount` "Já tenho conta", `validation.nameRequired` "Informe seu nome", `validation.nameTooLong` "O nome deve ter no máximo 120 caracteres", `validation.emailRequired` "Informe seu e-mail", `validation.emailInvalid` "E-mail inválido", `validation.emailTooLong` "O e-mail deve ter no máximo 254 caracteres", `validation.passwordLength` "A senha deve ter entre 8 e 128 caracteres", `validation.passwordMismatch` "As senhas não conferem", `common.showPassword` "Mostrar senha", `common.hidePassword` "Ocultar senha" (as remoções ficam para a US3)

**Checkpoint**: `dotnet build` em `backend/` e `npm run type-check; npm test` em `frontend/` verdes; o dev-login ainda funciona (removido só na US3).

---

## Phase 3: User Story 1 - Criar uma conta com e-mail e senha (Priority: P1) 🎯 MVP

**Goal**: `POST /auth/register` cria a conta (senha em hash PBKDF2) e já devolve o `LoginResult` (201); a tela pública `/cadastro` cadastra e entra direto, preservando `returnTo`.

**Independent Test**: na tela de entrada, seguir "Criar conta", cadastrar um e-mail novo e ver o cabeçalho com nome/e-mail e a lista de clientes vazia; `curl` do quickstart Cenário 1 (201 / 409 / 400).

### Tests for User Story 1 ⚠️

> Escrever primeiro e confirmar que falham.

- [X] T016 [P] [US1] Em `backend/Ambrosia.ApiTests/Controllers/AuthControllerTests.cs` adicionar a constante `REGISTER = "/auth/register"` e os testes (via `_fixture.PostAsync(REGISTER, string.Empty, new RegisterInfo { ... })`, e-mail `novo+{Guid:N}@exemplo.com`): `Register_NewEmail_ShouldReturn201WithToken` (201, `Token` não vazio, `ExpiresAt` futuro, `User.Provider == AuthProvider.Password`, `User.Email`/`Name` iguais aos enviados, token aceito em `GET /users/me` com o mesmo `id`, corpo bruto **sem** `password`/`passwordHash` — SC-007); `Register_DuplicateEmail_ShouldReturn409` (mensagem `"Este e-mail já está cadastrado."`); `Register_SameEmailDifferentCaseAndSpaces_ShouldReturn409` (`" NOVO+…@EXEMPLO.COM "` após cadastrar em minúsculas); `Register_ShortPassword_ShouldReturn400WithCampoPassword` (7 caracteres, mensagem `"A senha deve ter entre 8 e 128 caracteres."`); `Register_PasswordOver128_ShouldReturn400WithCampoPassword` (129 caracteres); `Register_EmptyName_ShouldReturn400WithCampoName` (`"O nome é obrigatório."`); `Register_NameOver120_ShouldReturn400WithCampoName`; `Register_InvalidEmail_ShouldReturn400WithCampoEmail` (`"E-mail inválido."`); `Register_MalformedBody_ShouldReturn400`
- [X] T017 [P] [US1] Adicionar a `frontend/src/lib/authForm.test.ts` os casos de `validateRegisterForm`: nome vazio/só espaços → `validation.nameRequired`; nome com 121 caracteres → `validation.nameTooLong`; e-mail vazio → `validation.emailRequired`; sem `@`/domínio → `validation.emailInvalid`; e-mail com 255 caracteres → `validation.emailTooLong`; senha com 7 e 129 caracteres → `validation.passwordLength`; 8 e 128 válidas; senha com espaços nas pontas preservada e válida; confirmação diferente → `validation.passwordMismatch`; tudo válido → objeto vazio
- [X] T018 [P] [US1] Adicionar a `frontend/src/Contexts/AuthContext.test.tsx` o caso `register` (mock de `authService.register`): grava a sessão em `localStorage` (`login-with-metamask:auth`) com `{ token, expiresAt, user }`, `isAuthenticated` vira `true`, devolve `true`; em `ApiError(409)` devolve `false`, expõe `error` com a mensagem da API e não grava sessão
- [X] T019 [P] [US1] Criar `frontend/src/pages/RegisterPage.test.tsx` (Vitest + RTL, `MemoryRouter`): senha curta mostra a mensagem no campo e **não** chama `register`; confirmação diferente mostra "As senhas não conferem"; envio válido chama `register(name.trim(), email.trim(), password)` com a senha como digitada e navega para `state.returnTo`; sem `returnTo` navega para `/clientes`; 409 mostra a mensagem da API em `role="alert"` e mantém as senhas nos campos; 400 com `erros[].campo = "email"` mostra a mensagem no campo e-mail; com sessão ativa redireciona para `/clientes`; link "Já tenho conta" leva a `/login` com `state.returnTo`

### Implementation for User Story 1

- [X] T020 [P] [US1] Criar `backend/Ambrosia.DTO/Auth/RegisterInfo.cs` com `Name`, `Email`, `Password` (`[JsonPropertyName("name")]`, `("email")`, `("password")`, strings não nulas com `string.Empty`), XML docs em português; nada de `userId`
- [X] T021 [US1] Criar `backend/Ambrosia.Domain/Validators/RegisterInfoValidator.cs` (depende de T020; FluentValidation, `CascadeMode.Stop`, padrão de `GoogleLoginInfoValidator.cs`): `Name` obrigatório e 1–120 caracteres **após `Trim()`** ("O nome é obrigatório." / "O nome deve ter no máximo 120 caracteres."); `Email` obrigatório, `EmailAddress()`, ≤ 254 ("O e-mail é obrigatório." / "E-mail inválido." / "O e-mail deve ter no máximo 254 caracteres."); `Password` obrigatório, 8–128 caracteres **sem trim e sem regra de composição** ("A senha é obrigatória." / "A senha deve ter entre 8 e 128 caracteres."); registrar no DI em `backend/Ambrosia.Application/Startup.cs` se os validators não forem registrados por varredura de assembly
- [X] T022 [US1] Adicionar `Task<LoginResult> RegisterAsync(RegisterInfo info)` a `backend/Ambrosia.Domain/Interfaces/IAuthService.cs` e implementar em `backend/Ambrosia.Domain/Services/AuthService.cs` (depende de T003, T004, T008, T021): validar com `RegisterInfoValidator` (`ValidateAndThrowAsync`); `User.NormalizeEmail(info.Email)` (trim + minúsculas); nome `Trim()`; senha **sem trim**; se o e-mail existe → `DuplicateException("Este e-mail já está cadastrado. Entre com sua conta Google.")` quando `Provider == AuthProvider.Google`, senão `DuplicateException("Este e-mail já está cadastrado.")` (nunca define senha na conta existente — FR-006); se não existe, criar `User` com `Provider = AuthProvider.Password`, `SetPassword(_passwordHash.Hash(info.Password), now)`, `InsertAsync` (a `DuplicateException` do índice `ux_users_email` na corrida é propagada → 409) e emitir o `LoginResult` com o mesmo `IJwtTokenAppService` do login Google; injetar `IPasswordHashAppService` no construtor
- [X] T023 [US1] Adicionar a ação `Register` em `backend/Ambrosia.API/Controllers/AuthController.cs` (depende de T022): `[HttpPost("register")]`, `[ProducesResponseType(typeof(LoginResult), 201)]`, `(typeof(ErrorResult), 400)`, `(typeof(ErrorResult), 409)`; `try { if (ModelStateError() is { } badRequest) return badRequest; return StatusCode(201, await _authService.RegisterAsync(info)); }` (sem `Location`) com `catch (ValidationException) → ValidationError`, `catch (DuplicateException) → ConflictError`, `catch (Exception) → StatusCode(500, ex.Message)`; XML doc com `<response>` de cada status
- [X] T024 [P] [US1] Atualizar o contrato consolidado `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml`: adicionar `paths./auth/register` (201 `LoginResult`, 400, 409 com os exemplos "senha" e "google", 500) e `components.schemas.RegisterInfo` (`name` 1–120, `email` format email ≤ 254, `password` format password 8–128 `writeOnly`) exatamente como em `contracts/auth-api.md`, e trocar o enum de `UserInfo.provider` para `[google, password]` (referência do `ContractAlignmentOperationFilter`)
- [X] T025 [P] [US1] Adicionar `register(info: RegisterInfo): Promise<LoginResult>` a `frontend/src/Services/authService.ts` (`POST /auth/register` anônimo pelo `apiClient`, DTO puro em 2xx — exceção herdada da 002 registrada no plan.md)
- [X] T026 [US1] Adicionar `register: (name: string, email: string, password: string) => Promise<boolean>` ao `AuthContextType` e ao provider em `frontend/src/Contexts/AuthContext.tsx` (depende de T025), reutilizando o `runLogin` existente (mesmo `writeSession`, mesmo timer de expiração, `loading`/`error`/`clearError` via `handleError`); `frontend/src/hooks/useAuth.ts` continua só repassando o contexto
- [X] T027 [US1] Implementar `validateRegisterForm({ name, email, password, confirmPassword })` em `frontend/src/lib/authForm.ts` (depende de T013): nome obrigatório após trim e ≤ 120; e-mail obrigatório, `/^[^\s@]+@[^\s@]+\.[^\s@]+$/` sobre o valor com trim e ≤ 254; senha 8–128 sem trim; confirmação igual à senha; devolve `{ name?, email?, password?, confirmPassword? }` com as chaves `validation.*`
- [X] T028 [US1] Criar `frontend/src/pages/RegisterPage.tsx` (depende de T014, T015, T026, T027): layout `max-w-md` com `Card`, `h1` `register.title`, subtítulo, erro geral em `role="alert"` (409/rede, via `error` do `useAuth`), campos Nome (`autoComplete="name"`), E-mail (`type="email"`, `autoComplete="email"`), Senha e Confirmar senha com `PasswordInput` (`autoComplete="new-password"`, `toggleLabels` de `common.showPassword`/`common.hidePassword`), erro de campo ligado por `aria-describedby` + `aria-invalid` e foco no primeiro campo inválido; envio só quando `validateRegisterForm` não tem erros; chama `register(name.trim(), email.trim(), password)`; erros de campo da API por `fieldErrorsOf`; botão desabilitado com `loading` e "Carregando" em `aria-live="polite"`; `clearError()` ao editar; senhas mantidas após erro; com sessão → `<Navigate to={returnTo ?? '/clientes'} replace />`; link "Já tenho conta" → `/login` com `state: { returnTo }`; todas as strings de `pt-BR.ts`; sem rolagem horizontal a 360 px
- [X] T029 [US1] Registrar a rota pública `/cadastro` → `RegisterPage` em `frontend/src/App.tsx` (fora de `RequireAuth`, ao lado de `/login`, com o mesmo padrão de import — lazy ou direto — usado por `LoginPage`)
- [X] T030 [US1] Adicionar em `frontend/src/pages/LoginPage.tsx` a linha "Não tem conta? Criar conta" (`login.noAccount` + `Link` `login.createAccount` → `/cadastro` com `state: { returnTo }`) para cumprir o US1 cenário 7 / FR-018

**Checkpoint**: `dotnet test Ambrosia.ApiTests --filter Register` verde contra Development; `npm test` verde; cadastro manual em `http://localhost:5173/cadastro` entra direto (quickstart Cenário 4, passos 1–3 e 6–7).

---

## Phase 4: User Story 2 - Entrar com e-mail e senha (Priority: P1)

**Goal**: `POST /auth/login` devolve o mesmo `LoginResult` do Google com 401 genérico e tempo uniforme; a tela `/login` ganha o formulário de e-mail/senha ao lado do Google; o login Google remove a senha de contas reutilizadas (FR-009).

**Independent Test**: com uma conta criada, sair, entrar com e-mail e senha e ver os mesmos dados; senha errada e e-mail inexistente mostram "E-mail ou senha inválidos" na própria tela (não "Sua sessão expirou").

### Tests for User Story 2 ⚠️

- [X] T031 [P] [US2] Em `backend/Ambrosia.ApiTests/Controllers/AuthControllerTests.cs` adicionar `LOGIN = "/auth/login"` e os testes (cada um cadastra seu usuário por `POST /auth/register` com e-mail GUID): `Login_ValidCredentials_ShouldReturn200WithSameUserId` (200, mesmo `User.Id` do cadastro, `Provider == Password`, token aceito em `/users/me`, corpo sem `password`/`passwordHash`); `Login_EmailWithDifferentCaseAndSpaces_ShouldReturn200`; `Login_WrongPassword_ShouldReturn401Generic` (`mensagem == "E-mail ou senha inválidos"`, `erros` vazio); `Login_UnknownEmail_ShouldReturn401WithSameMessage` (mensagem **idêntica** à da senha errada — SC-006); `Login_EmptyBody_ShouldReturn400`; `Login_InvalidEmail_ShouldReturn400WithCampoEmail`; `Login_MissingPassword_ShouldReturn400WithCampoPassword` ("A senha é obrigatória."); manter os testes existentes de `/auth/google` inválido/ausente (400)
- [X] T032 [P] [US2] Adicionar a `frontend/src/lib/authForm.test.ts` os casos de `validateLoginForm`: e-mail vazio → `validation.emailRequired`; e-mail mal formado → `validation.emailInvalid`; senha vazia → `validation.passwordRequired` ("Informe sua senha", chave adicionada a `pt-BR.ts` em T036); senha curta **é aceita** no login (comprimento não validado — data-model §3); tudo válido → objeto vazio
- [X] T033 [P] [US2] Adicionar a `frontend/src/Contexts/AuthContext.test.tsx` o caso `loginWithPassword`: sucesso grava a sessão e devolve `true`; `ApiError(401)` devolve `false`, expõe `error = "E-mail ou senha inválidos"` e **não** executa `logout('expirada')` nem mostra o aviso de sessão expirada
- [X] T034 [P] [US2] Reescrever `frontend/src/pages/LoginPage.test.tsx` sem nenhuma referência a `VITE_DEV_LOGIN`/`DevLoginForm`: envio válido chama `loginWithPassword(email.trim(), password)` e navega para `state.returnTo` (ou `/clientes`); campo inválido mostra a mensagem e não envia; 401 mostra a mensagem em `role="alert"` e a senha continua no campo; `?motivo=expirada` continua mostrando "Sua sessão expirou"; link "Criar conta" leva a `/cadastro` com `state.returnTo`; sem `VITE_GOOGLE_CLIENT_ID` não há botão Google nem separador "ou"; com ele, o botão aparece acima do formulário

### Implementation for User Story 2

- [X] T035 [P] [US2] Criar `backend/Ambrosia.DTO/Auth/LoginInfo.cs` com `Email` e `Password` (`[JsonPropertyName("email")]`, `("password")`), XML docs em português
- [X] T036 [US2] Criar `backend/Ambrosia.Domain/Validators/LoginInfoValidator.cs` (depende de T035; `CascadeMode.Stop`): `Email` obrigatório e `EmailAddress()` ("O e-mail é obrigatório." / "E-mail inválido."); `Password` obrigatório ("A senha é obrigatória."), **comprimento não validado** (qualquer senha errada cai no 401 genérico); registrar no DI em `backend/Ambrosia.Application/Startup.cs` se necessário. No frontend, adicionar `validation.passwordRequired` "Informe sua senha" a `frontend/src/i18n/locales/pt-BR.ts`
- [X] T037 [US2] Adicionar `Task<LoginResult> LoginWithPasswordAsync(LoginInfo info)` a `backend/Ambrosia.Domain/Interfaces/IAuthService.cs` e implementar em `backend/Ambrosia.Domain/Services/AuthService.cs` (depende de T005, T008, T036): validar; buscar pelo e-mail normalizado; **sempre exatamente uma** verificação PBKDF2 — se o usuário não existe ou `!HasPassword`, `_passwordHash.Verify(info.Password, _passwordHash.DummyHash)` e lançar `InvalidCredentialsException`; se existe com senha e `Verify` falha, lançar `InvalidCredentialsException`; se confere, `Touch(now)`, `UpdateAsync` e emitir o `LoginResult` com o mesmo `IJwtTokenAppService` (mesmos claims, 8 h — FR-007); senha nunca registrada em log
- [X] T038 [US2] Alterar `LoginWithGoogleAsync` em `backend/Ambrosia.Domain/Services/AuthService.cs` (depende de T004): ao reutilizar conta existente, além de `UpdateFromProvider(name, subject, now)`, definir `Provider = AuthProvider.Google` e chamar `ClearPassword(now)` quando `HasPassword` (FR-009; invariante `Provider = Google ⇒ PasswordHash = NULL`); criação de conta nova pelo Google inalterada
- [X] T039 [US2] Adicionar a ação `Login` em `backend/Ambrosia.API/Controllers/AuthController.cs` (depende de T009, T037): `[HttpPost("login")]`, `[ProducesResponseType(typeof(LoginResult), 200)]`, `(typeof(ErrorResult), 400)`, `(typeof(ErrorResult), 401)`; `ModelStateError()` → 400; `catch (ValidationException) → ValidationError`, `catch (InvalidCredentialsException) → InvalidCredentialsError`, `catch (Exception) → StatusCode(500, ex.Message)`; atualizar o XML doc da ação `Google` com a regra FR-009 (descrição de contracts/auth-api.md) e o resumo da classe (`/auth/*` é o único grupo público)
- [X] T040 [P] [US2] Atualizar `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml`: adicionar `paths./auth/login` (200 `LoginResult`, 400, 401 `ErrorResult` com exemplo `"E-mail ou senha inválidos"`, 500) e `components.schemas.LoginInfo` (`email` format email, `password` format password `writeOnly`), e a nova `description` de `/auth/google` (FR-009), conforme `contracts/auth-api.md`
- [X] T041 [P] [US2] Adicionar `loginWithPassword(info: LoginInfo): Promise<LoginResult>` a `frontend/src/Services/authService.ts` (`POST /auth/login` anônimo)
- [X] T042 [US2] Adicionar `loginWithPassword: (email: string, password: string) => Promise<boolean>` ao `AuthContextType` e ao provider em `frontend/src/Contexts/AuthContext.tsx` (depende de T012, T041), via `runLogin`; garantir que o `ApiError(401)` vira `error` do formulário e não dispara `logout('expirada')`
- [X] T043 [US2] Implementar `validateLoginForm({ email, password })` em `frontend/src/lib/authForm.ts` (depende de T013): e-mail obrigatório + formato básico (mesma regex de T027) sobre o valor com trim; senha obrigatória (`validation.passwordRequired`), sem limite de comprimento
- [X] T044 [US2] Reformular `frontend/src/pages/LoginPage.tsx` (depende de T014, T042, T043): botão Google (`useGoogleIdentity`, inalterado) **acima** do formulário com separador `login.or`, ambos ocultos quando `VITE_GOOGLE_CLIENT_ID` está vazio; erro geral em `role="alert"`; campo E-mail (`type="email"`, `autoComplete="username"`) e Senha com `PasswordInput` (`autoComplete="current-password"`); validação por `validateLoginForm` com mensagem e foco no campo inválido; envio `loginWithPassword(email.trim(), password)`; erros de campo da API via `fieldErrorsOf`; botão `login.submit` desabilitado com `loading` + "Carregando" em `aria-live="polite"` (`min-h-5`); `clearError()` ao editar; senha mantida após erro; aviso de `?motivo=expirada` preservado; com sessão → `<Navigate to={returnTo ?? '/clientes'} replace />`. **Não** remover ainda o `lazy()` do `DevLoginForm` (feito em T060)

**Checkpoint**: `dotnet test Ambrosia.ApiTests --filter "Register|Login"` verde; quickstart Cenário 1 (201 · 200 · 401 · 401 · 409 · 400) e Cenário 4 passos 4–5 passam; Google continua funcionando (SC-009).

---

## Phase 5: User Story 4 - Testes de API criam o usuário e entram na sequência (Priority: P2)

> Executada **antes** da US3: sem esta fase, remover o dev-login deixa a suíte inteira sem credencial.

**Goal**: o `ApiTestFixture` cria A e B por cadastro + login (e-mail único por execução); nenhum teste usa atalho sem senha.

**Independent Test**: `dotnet test Ambrosia.ApiTests` contra Development duas vezes seguidas, sem usuário criado manualmente → 100% verde (quickstart Cenário 2).

### Implementation for User Story 4

- [X] T045 [P] [US4] Em `backend/Ambrosia.ApiTests/appsettings.Test.json`: remover `Auth:DevLoginEndpoint`; adicionar `Auth:RegisterEndpoint` `"/auth/register"`, `Auth:LoginEndpoint` `"/auth/login"` e `Auth:TestPassword` `"Senha-de-teste-123"` (não é segredo: só existe em bancos de Development/Homolog — research §7)
- [X] T046 [US4] Reescrever o fluxo de autenticação de `backend/Ambrosia.ApiTests/Fixtures/ApiTestFixture.cs` (depende de T045 e das fases 3–4): trocar `DevLoginEndpoint` por `RegisterEndpoint`/`LoginEndpoint` e adicionar `TestPassword` lidos da configuração; `RegisterAsync(string email, string name, string password)` → `POST` de `RegisterInfo`, exige 201 (mensagem de erro com status e corpo); `LoginAsync(string email, string password)` → `POST` de `LoginInfo`, exige 200 e token não vazio; `CreateUserAsync(string email, string name)` = `RegisterAsync` **seguido de** `LoginAsync`, devolvendo o `LoginResult` do **login**; `InitializeAsync` cria A (`ana+<guid8>@exemplo.com`, "Ana") e B (`bia+<guid8>@exemplo.com`, "Bia") por `CreateUserAsync`; apagar `DevLoginAsync` e a mensagem de 404 do dev-login; reescrever o XML doc da classe (sem menção a dev-login; senha de teste não secreta)
- [X] T047 [P] [US4] Substituir chamadas a `_fixture.DevLoginAsync(...)` por `_fixture.CreateUserAsync(...)` (usuário extra, ex.: mesmo CPF para dois usuários) em `backend/Ambrosia.ApiTests/Controllers/CustomersControllerTests.cs`, `backend/Ambrosia.ApiTests/Controllers/IsolationTests.cs`, `backend/Ambrosia.ApiTests/Controllers/UsersControllerTests.cs` e `backend/Ambrosia.ApiTests/Controllers/OrdersControllerTests.cs` (onde houver; conferir com `git grep DevLoginAsync backend/Ambrosia.ApiTests`), e ajustar asserções de `provider` para `AuthProvider.Password`
- [X] T048 [P] [US4] Em `backend/Ambrosia.ApiTests/Controllers/UnauthorizedAccessTests.cs`, o teste das rotas públicas passa a chamar `POST /auth/login` sem `Authorization` com um usuário recém-criado por `_fixture.RegisterAsync` e esperar 200 (em vez do dev-login)
- [X] T049 [US4] Em `backend/Ambrosia.ApiTests/Controllers/AuthControllerTests.cs`: apagar os testes `DevLogin_*` e a constante `DEV_LOGIN`; adicionar `CreateUserAsync_ShouldRegisterAndLoginInSequence` (o `LoginResult` devolvido pelo fixture tem o mesmo `User.Id` de `/users/me` e `Provider == Password` — US4 cenário 2); atualizar o XML doc da classe para "cadastro, login com senha e Google"
- [X] T050 [US4] Com a API em Development (`backend/scripts/run-api.ps1`), rodar `dotnet test Ambrosia.ApiTests` em `backend/` **duas vezes seguidas** e corrigir no código qualquer falha (nunca desabilitar testes — constituição §VII); confirmar que nenhum arquivo de `backend/Ambrosia.ApiTests/` contém `dev-login`/`DevLogin`

**Checkpoint**: suíte de API 100% verde em Development sem depender do dev-login — já é seguro removê-lo.

---

## Phase 6: User Story 3 - Entrada de desenvolvimento desaparece de todos os ambientes (Priority: P1)

**Goal**: nenhum caminho sem senha em nenhum ambiente: rota, DTO, validator, filtro do Swagger, flag de build, formulário, requisição do Bruno e documentação removidos; `verify:prod` cobre todos os modos.

**Independent Test**: `POST /auth/dev-login` → 404 em Development e Homolog; Swagger sem a rota; `verify:prod` OK após build em development, homolog e production; `git grep` do quickstart Cenário 8 vazio fora de `specs/`.

### Tests for User Story 3 ⚠️

- [X] T051 [P] [US3] Em `backend/Ambrosia.ApiTests/Controllers/AuthControllerTests.cs` adicionar `DevLogin_ShouldReturn404InAnyEnvironment` (`POST /auth/dev-login` com `{ email, name }` anônimo → 404, corpo vazio, igual a `POST /auth/rota-inexistente`) e `Swagger_ShouldNotListDevLogin` (`GET swagger/v1/swagger.json` → 200, contém `/auth/register` e `/auth/login` e **não** contém `dev-login` nem `DevLoginInfo`) — devem falhar até T053–T055

### Implementation for User Story 3 — backend

- [X] T052 [P] [US3] Apagar `backend/Ambrosia.DTO/Auth/DevLoginInfo.cs` e `backend/Ambrosia.Domain/Validators/DevLoginInfoValidator.cs`; remover `DevLoginAsync` de `backend/Ambrosia.Domain/Interfaces/IAuthService.cs` e de `backend/Ambrosia.Domain/Services/AuthService.cs` (e o registro do validator no `Startup.cs`, se houver)
- [X] T053 [US3] Em `backend/Ambrosia.API/Controllers/AuthController.cs` (depende de T052): apagar a ação `DevLogin`, o campo/parâmetro `IHostEnvironment` do construtor e o `using Ambrosia.API.Extensions`; a rota passa a responder 404 pelo roteamento normal, sem código condicional por ambiente (FR-012)
- [X] T054 [US3] Apagar `backend/Ambrosia.API/Swagger/DevLoginDocumentFilter.cs` e, em `backend/Ambrosia.API/Program.cs`, remover `options.DocumentFilter<DevLoginDocumentFilter>()` e trocar a descrição do esquema Bearer por "Cole o token do Ambrosia obtido em `POST /auth/login`, `POST /auth/register` ou `POST /auth/google`. Não inclua o prefixo `Bearer`."
- [X] T055 [US3] Apagar `backend/Ambrosia.API/Extensions/HostEnvironmentExtensions.cs` (depende de T053, T054) após confirmar com `git grep -n "HostEnvironmentExtensions\|IsDevLoginEnabled" backend` que nem `IsDevLoginEnabled` nem a constante `Homolog` têm outro uso; se a constante for usada, manter o arquivo só com ela
- [X] T056 [P] [US3] Conferir com `git grep -n "AuthProvider\|DevLogin\|AuthService" backend/Ambrosia.Tests` que nenhum teste unitário remanescente referencia a autenticação (os arquivos foram **apagados** em T003, nunca ajustados) e registrar em `specs/003-email-password-jwt-auth/decisions.md` (seção "tasks · follow-up §VIII") o follow-up de remover o projeto `Ambrosia.Tests` inteiro em ciclo próprio; **nenhum teste unitário do backend é criado, ajustado nem executado** (constituição §VIII)
- [X] T057 [US3] Remover de `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml` o `paths./auth/dev-login` e `components.schemas.DevLoginInfo`; rodar `dotnet build` em `backend/` (a solução inteira compila; `Ambrosia.Tests` não é executado — §VIII) e, com a API em Development, `dotnet test Ambrosia.ApiTests` (T051 deve passar)

### Implementation for User Story 3 — frontend e build

- [X] T058 [P] [US3] Apagar `frontend/src/pages/DevLoginForm.tsx`; remover de `frontend/src/pages/LoginPage.tsx` o `lazy()`/`Suspense` condicional do `DevLoginForm` e qualquer leitura de `import.meta.env.VITE_DEV_LOGIN`
- [X] T059 [P] [US3] Remover `loginDev` de `frontend/src/Services/authService.ts` e de `frontend/src/Contexts/AuthContext.tsx` (tipo `AuthContextType` incluído), `DevLoginInfo` de `frontend/src/types/auth.ts` e qualquer menção a dev-login em `frontend/src/Services/apiClient.test.ts` e `frontend/src/Contexts/AuthContext.test.tsx`
- [X] T060 [P] [US3] Remover de `frontend/src/i18n/locales/pt-BR.ts` as chaves `login.dev`, `login.name` (substituída por `register.name`) e `errors.devLoginUnavailable`, conferindo com `git grep` que nenhum componente as usa
- [X] T061 [P] [US3] Remover `VITE_DEV_LOGIN` de `frontend/src/vite-env.d.ts`, `frontend/.env.example`, `frontend/.env.development`, `frontend/.env.homolog` e `frontend/.env.production`
- [X] T062 [P] [US3] Em `frontend/scripts/verify-prod.mjs`, acrescentar `'VITE_DEV_LOGIN'` à lista `FORBIDDEN` (mantendo `dev-login` e `DevLoginForm`), manter o nome do script e o orçamento de bundle, e ajustar a mensagem de sucesso para `OK — nenhuma ocorrência de "dev-login" / "DevLoginForm" / "VITE_DEV_LOGIN"`
- [X] T063 [US3] Em `frontend/Dockerfile` (skill `dotnet-env`): remover `ARG VITE_DEV_LOGIN`/`ENV VITE_DEV_LOGIN` e executar `node scripts/verify-prod.mjs` (ou `npm run verify:prod`) **incondicionalmente** após o `npm run build` em qualquer modo (homolog e production — research §9)
- [X] T064 [P] [US3] Remover o build arg `VITE_DEV_LOGIN` do serviço do frontend em `docker-compose.yml` e `docker-compose-prod.yml`
- [X] T065 [US3] Validar o frontend (depende de T058–T064): em `frontend/`, `npm run lint; npm run type-check; npm test` e, para cada modo `development`, `homolog` e `production`, `npm run build -- --mode <modo>; npm run verify:prod` — todos OK (SC-004); `git grep -n -i "dev-login\|VITE_DEV_LOGIN\|DevLogin" -- frontend` vazio

### Implementation for User Story 3 — Bruno

- [X] T066 [P] [US3] Criar `bruno/Auth/Criar conta.bru` (`meta { seq: 1 }`, `POST {{baseUrl}}/auth/register`, corpo JSON `{ "name": "{{userName}}", "email": "{{userEmail}}", "password": "{{userPassword}}" }`, `script:post-response` grava `bru.setEnvVar("token", res.body.token)` só se `res.status === 201`, `docs` explicando o 409 nas execuções seguintes) e `bruno/Auth/Login com senha.bru` (`seq: 2`, `POST {{baseUrl}}/auth/login`, corpo `{ "email": "{{userEmail}}", "password": "{{userPassword}}" }`, grava `token` se 200), seguindo o formato de `bruno/Auth/Google Login.bru`
- [X] T067 [P] [US3] Apagar `bruno/Auth/Dev Login.bru` e mudar `bruno/Auth/Google Login.bru` para `seq: 3`
- [X] T068 [P] [US3] Em `bruno/environments/Homolog.bru`: trocar `devEmail`/`devName` por `userEmail` `homolog@ambrosia.test` e `userName` `Homologação` em `vars`, e incluir `userPassword` em `vars:secret` junto de `token` e `googleIdToken`; em `bruno/collection.bru`: mesmas variáveis em `vars:pre-request` (removendo qualquer token hard-coded), e `docs` com o passo 2 "Criar conta na primeira vez, Login com senha nas seguintes" (FR-014)

### Implementation for User Story 3 — documentação e constituição (FR-015)

- [X] T069 [P] [US3] Remover o dev-login como forma de acesso de `backend/README.md` (tabela de `ASPNETCORE_ENVIRONMENT` e passo 3 do fluxo → cadastro/login com senha), `frontend/README.md` (scripts, variáveis, build Docker: sem `VITE_DEV_LOGIN`; `verify:prod` em todos os modos), `deploy/README.md` e do comentário de `backend/scripts/seed-load-test.sql`; conferir também `README.md` da raiz
- [X] T070 [P] [US3] Atualizar `docs/README.md`; em `docs/historias/001-api-cadastro-clientes-pedidos.md` e `docs/historias/002-frontend-web-perfil-cliente-pedidos.md` acrescentar nota "substituído pela 003: cadastro e login com e-mail e senha" sem reescrever o histórico; em `docs/historias/007-oauth-mcp-chatgpt.md` e `docs/historias/009-bot-migracao-mais-vendas.md` observar que o dev-login não existe mais e indicar `/auth/register` + `/auth/login`
- [X] T071 [P] [US3] Atualizar `MAESTRO.md`: comentário do `npm run verify:prod` passa a "prova do pacote: falha se dev-login/DevLoginForm/VITE_DEV_LOGIN sobreviver no dist/ (qualquer modo)"; manter a seção `MANUAL ADDITIONS` intacta
- [X] T072 [US3] Aplicar a emenda de `specs/003-email-password-jwt-auth/constitution-amendment.md` em `.specify/memory/constitution.md`: §VI com cadastro/login por e-mail e senha (hash salgado, nunca exposto) ao lado do Google e **proibição** do `/auth/dev-login` em todos os ambientes; versão 3.2.0 → 3.3.0 (MINOR), data de emenda e Sync Impact Report atualizados

**Checkpoint**: `git grep -n -i "dev-login\|devLogin\|VITE_DEV_LOGIN\|devEmail" -- . ":!specs/" ":!backend/.tmp-stage"` vazio (quickstart Cenário 8); backend e frontend verdes.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: encerramento do ciclo (constituição §VII) e validação ponta a ponta.

- [X] T073 Reconstruir a homologação pela skill `wsl-docker`: `wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build` (o build do frontend deve executar o `verify-prod` e passar); conferir `curl -o NUL -w "%{http_code}" -X POST http://localhost:8080/api/auth/dev-login` → 404 e que `http://localhost:8080/api/swagger` não lista a rota
- [X] T074 Rodar a suíte de API contra Homolog **duas vezes seguidas** (em `backend/`: `$env:ApiBaseUrl = "http://localhost:8080/api"; dotnet test Ambrosia.ApiTests; dotnet test Ambrosia.ApiTests; Remove-Item Env:ApiBaseUrl`) → 100% verde sem usuário criado manualmente (SC-005); corrigir no código qualquer falha
- [ ] T075 [P] Rodar a coleção do Bruno contra Homolog (`cd bruno; bru run --env Homolog`, com `userPassword` preenchido): "Criar conta" 201 na primeira execução e 409 nas seguintes, "Login com senha" 200 gravando `token`, Users/Customers/Orders passam (SC-008)
- [X] T076 [P] Medir `POST /auth/login` e `/auth/register` em Homolog (ex.: 20 chamadas com `curl -w "%{time_total}"`): p95 ≤ 500 ms e tempos de senha errada ≈ e-mail inexistente (SC-006, Performance Goals); registrar os números em `specs/003-email-password-jwt-auth/decisions.md`
- [X] T077 [P] Verificar SC-007: respostas de `/auth/register`, `/auth/login`, `/auth/google` e `/users/me` não contêm `password` nem `passwordHash` (inspeção dos corpos dos testes de API e do Swagger)
- [ ] T078 Executar manualmente o quickstart Cenário 4 (dev server, 360 px, foco visível, Mostrar/Ocultar, returnTo, `/cadastro` já conectada) e, se houver credenciais Google, o Cenário 7 (FR-009); conferir que o bundle inicial continua ≤ 250 kB gzip na saída do `verify:prod`
- [X] T079 Revisão final: `npm run lint; npm run type-check; npm test` em `frontend/`, `dotnet build` em `backend/` sem warnings novos, e marcação das tarefas concluídas neste `tasks.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências
- **Foundational (Phase 2)**: depende do Setup — BLOQUEIA todas as histórias
- **US1 (Phase 3)** e **US2 (Phase 4)**: dependem da Phase 2; podem andar em paralelo (arquivos compartilhados: `AuthService.cs`, `IAuthService.cs`, `AuthController.cs`, `AuthControllerTests.cs`, `authService.ts`, `AuthContext.tsx`, `authForm.ts`, `openapi.yaml`, `LoginPage.tsx` — coordenar as edições nesses arquivos)
- **US4 (Phase 5)**: depende de US1 e US2 (o fixture usa `/auth/register` e `/auth/login`)
- **US3 (Phase 6)**: depende de US4 para o backend (T052–T057 só depois de T050); a parte de frontend (T058–T065), Bruno (T066–T068) e docs (T069–T072) depende apenas de US1/US2 (T030, T044, que substituem o formulário de desenvolvimento) e pode começar em paralelo com a US4
- **Polish (Phase 7)**: depende de todas as histórias

### User Story Dependencies

- **US1 (P1)**: só Foundational
- **US2 (P1)**: só Foundational; a tela de entrada recebe o link de cadastro da US1 (T030) — integrar sem conflito com T044
- **US4 (P2)**: US1 + US2
- **US3 (P1)**: US4 (backend) e US1/US2 (frontend)

### Within Each User Story

- Testes escritos primeiro e falhando
- DTO → validator → serviço de domínio → controller → contrato OpenAPI
- Frontend: tipo → service → context → validação pura → página → rota
- Checkpoint verificado antes da próxima fase

### Parallel Opportunities

- Phase 2: T003, T004, T005, T006, T009 (backend) e T010, T011, T013, T014, T015 (frontend) em paralelo; T007 → T008 em sequência; T011 → T012
- US1: testes T016–T019 em paralelo; T020, T024, T025 em paralelo
- US2: testes T031–T034 em paralelo; T035, T040, T041 em paralelo
- US4: T045, T047, T048 em paralelo após T046 ser planejada (T047/T048 usam a API nova do fixture)
- US3: blocos frontend (T058–T062, T064), Bruno (T066–T068) e docs (T069–T071) em paralelo entre si

---

## Parallel Example: User Story 1

```text
# Testes da US1 juntos:
Task: "T016 [US1] Testes de /auth/register em backend/Ambrosia.ApiTests/Controllers/AuthControllerTests.cs"
Task: "T017 [US1] Casos de validateRegisterForm em frontend/src/lib/authForm.test.ts"
Task: "T018 [US1] Caso register em frontend/src/Contexts/AuthContext.test.tsx"
Task: "T019 [US1] frontend/src/pages/RegisterPage.test.tsx"

# Peças independentes da US1 juntas:
Task: "T020 [US1] RegisterInfo em backend/Ambrosia.DTO/Auth/RegisterInfo.cs"
Task: "T024 [US1] /auth/register em specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml"
Task: "T025 [US1] register em frontend/src/Services/authService.ts"
```

## Parallel Example: User Story 3 (após a US4)

```text
Task: "T058 [US3] Apagar frontend/src/pages/DevLoginForm.tsx"
Task: "T061 [US3] Remover VITE_DEV_LOGIN de frontend/.env.* e vite-env.d.ts"
Task: "T064 [US3] Remover build arg de docker-compose.yml e docker-compose-prod.yml"
Task: "T066 [US3] Criar bruno/Auth/Criar conta.bru e Login com senha.bru"
Task: "T069 [US3] Limpar READMEs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 + Phase 2
2. Phase 3 (US1): cadastro na API e na tela `/cadastro`, entrando direto
3. **PARAR e VALIDAR**: testes de US1 + quickstart Cenário 1 (201/409/400) e Cenário 4 passos 1–3
4. O MVP já permite criar contas reais; o dev-login ainda existe até a Phase 6

### Incremental Delivery

1. Setup + Foundational → base pronta
2. US1 → cadastro funcionando (MVP)
3. US2 → login diário com senha + FR-009 no Google
4. US4 → suíte de API independente do dev-login
5. US3 → remoção completa do dev-login (backend, frontend, build, Bruno, docs, constituição)
6. Polish → homolog reconstruída, ApiTests ×2 contra Homolog, Bruno, medições

### Parallel Team Strategy

1. Todos: Setup + Foundational
2. Dev A: backend de US1 → US2 → US4; Dev B: frontend de US1 → US2
3. Dev B segue para frontend/Bruno/docs da US3 enquanto Dev A termina a US4; Dev A faz o backend da US3
4. Juntos: Phase 7

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente
- [Story] liga a tarefa à história para rastreabilidade
- Senha nunca em texto puro em banco, log, DTO de saída ou tela (FR-005, constituição §VI)
- Nenhuma migração EF, nenhum pacote novo (NuGet ou npm), nenhuma variável de ambiente nova
- Commits em português, após cada tarefa ou grupo lógico
