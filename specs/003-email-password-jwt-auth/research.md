# Research — 003-email-password-jwt-auth

Phase 0 do plano. Todas as incógnitas do Technical Context foram resolvidas abaixo; nenhuma
permanece como NEEDS CLARIFICATION. Cada item traz decisão, justificativa e alternativas.

## §1. Algoritmo e formato do hash de senha

**Decisão**: PBKDF2-HMAC-SHA256 via `System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2`
(BCL do .NET 8), **600.000 iterações**, salt aleatório de 16 bytes (`RandomNumberGenerator`),
derivação de 32 bytes. Persistido em `users.password_hash` como texto autodescritivo:

```text
pbkdf2-sha256$600000$<salt base64>$<hash base64>
```

(~90 caracteres; cabe com folga no `varchar(260)` existente). A comparação usa
`CryptographicOperations.FixedTimeEquals`. Implementado em `PasswordHashAppService`
(`Ambrosia.Infra/AppServices`) atrás de `IPasswordHashAppService` (`Ambrosia.Infra.Interfaces`),
registrado como singleton no `Startup.cs`.

**Justificativa**: zero dependência nova (constituição §II, stack fixa); PBKDF2 com 600k
iterações SHA-256 é a recomendação OWASP vigente; o prefixo com algoritmo e iterações permite
rehash futuro sem migração; AppService na Infra segue o mesmo padrão de `CpfProtectionAppService`.
O custo (~150–300 ms por verificação) é deliberado e cabe na meta de 500 ms p95.

**Alternativas**: `Microsoft.AspNetCore.Identity.PasswordHasher` (traz o pacote Identity.Core e
um formato binário opaco); BCrypt.Net-Next / Argon2 (pacotes de terceiros; Argon2id seria o
ideal técnico, mas exige dependência nativa/terceiro). Rejeitadas por adicionarem dependência
sem ganho necessário para o porte do produto.

## §2. Tempo de resposta uniforme no login inválido

**Decisão**: `AuthService.LoginWithPasswordAsync` sempre executa **uma** verificação PBKDF2:
quando o usuário não existe ou não tem senha (`PasswordHash == null`), verifica a senha
informada contra um **hash fictício** pré-computado no construtor do `PasswordHashAppService`
(`DummyHash`), e então lança `InvalidCredentialsException`. Resultado: e-mail inexistente,
senha errada e conta só-Google custam o mesmo (FR-008, SC-006, edge case "tentativas repetidas").

**Alternativas**: `Task.Delay` fixo (impreciso e mascara menos o custo real); nada (vazaria
a existência do e-mail pelo tempo). Rate limiting continua fora do escopo (Assumptions).

## §3. Status HTTP e tratamento no frontend

**Decisão**:

| Situação | Status | Corpo |
|---|---|---|
| Cadastro criado | **201 Created** | `LoginResult` (sem `Location`: o recurso é a sessão, não um `/users/{id}`) |
| Login OK | 200 | `LoginResult` |
| Campos inválidos (cadastro ou login) | 400 | `ErrorResult` com `erros[]` por campo |
| E-mail já cadastrado | **409 Conflict** | `ErrorResult` com `mensagem` = "Este e-mail já está cadastrado." (ou "… Entre com sua conta Google." quando a conta existente é Google) e `erros` = `[]` |
| Credencial inválida (e-mail inexistente / senha errada / só-Google) | **401 Unauthorized** | `ErrorResult` com `mensagem` = "E-mail ou senha inválidos", `erros` = `[]` |
| `/auth/dev-login` | 404 | vazio (rota inexistente, igual a qualquer outra) |

No frontend, o `apiClient` hoje dispara o handler de 401 (→ `logout('expirada')`) em qualquer
resposta 401. **Ajuste**: quando a chamada é anônima (`withAuth === false`, caso de todas as
`/auth/*`), um 401 **não** dispara o handler e vira `ApiError(401, mensagem)` lido do
`ErrorResult`; o `AuthContext` exibe `error` no formulário. Coberto por teste unitário em
`apiClient.test.ts`.

**Justificativa**: 401 é o status correto para credencial recusada; 409 reaproveita
`ConflictError`/`DuplicateException` já existentes; 400 continua reservado à validação de
formato, o que mantém o mapeamento campo→mensagem do formulário.

**Alternativas**: 400 para credencial inválida (mistura validação e autenticação); 200 com
`sucesso:false` (contraria o contrato de DTO puro da HU 001); 404 em e-mail inexistente
(revelaria a existência do e-mail — proibido pela FR-008).

## §4. Modelo de usuário: provedor, senha e reutilização

**Decisão**:

- `AuthProvider`: `Google = 1` mantido; `Dev = 2` **renomeado para `Password = 2`** (JSON
  `"password"`). Nenhuma migração: o valor inteiro é o mesmo. Usuários antigos criados por
  dev-login ficam com `provider = 2` e `password_hash = NULL`: não entram por senha (hash nulo →
  §2), e o cadastro com o mesmo e-mail responde 409 genérico (edge case da spec).
- `User` ganha `SetPassword(hash, now)`, `ClearPassword(now)` e `HasPassword`. `PasswordHash`
  continua sem mapeamento para `UserInfo`.
- **Cadastro** (`RegisterAsync`): e-mail normalizado (`User.NormalizeEmail`), nome `Trim()`,
  senha **sem trim** (edge case). Se já existe: `Provider == Google` → `DuplicateException`
  com a mensagem que orienta a entrar com Google; senão → `DuplicateException` genérica.
  Se não existe: `InsertAsync` com `Provider = Password`; `DuplicateException` do índice único
  na corrida de cadastros simultâneos é propagada como 409 (apenas uma conta é criada).
- **Login com senha** (`LoginWithPasswordAsync`): busca pelo e-mail normalizado; verifica
  (§2); atualiza `UpdatedAt`; emite `LoginResult`.
- **Login Google** (`LoginWithGoogleAsync`, existente): ao reutilizar uma conta, além de
  `UpdateFromProvider(name, subject)`, passa a fazer `Provider = Google` e `ClearPassword()`
  quando havia senha (FR-009). Sessões já emitidas por senha seguem válidas até expirar
  (sem invalidação retroativa — Assumptions).

**Alternativas**: novo enum `Dev = 2, Password = 3` com migração de dados (desnecessário: os
usuários dev são descartados na reconstrução da homolog); vincular senha à conta Google no
cadastro (rejeitado na clarificação por risco de sequestro sem verificação de e-mail).

## §5. Validação dos DTOs de entrada (backend)

**Decisão** (FluentValidation, `CascadeMode.Stop`, mensagens em português citando o campo,
mesmo estilo de `DevLoginInfoValidator` que é removido):

| DTO | Campo | Regras | Mensagens |
|---|---|---|---|
| `RegisterInfo` | `name` | obrigatório, 1–120 após trim | "O nome é obrigatório." / "O nome deve ter no máximo 120 caracteres." |
| | `email` | obrigatório, `EmailAddress()`, ≤ 254 | "O e-mail é obrigatório." / "E-mail inválido." / "O e-mail deve ter no máximo 254 caracteres." |
| | `password` | obrigatório, 8–128 caracteres (sem trim, sem exigência de composição) | "A senha é obrigatória." / "A senha deve ter entre 8 e 128 caracteres." |
| `LoginInfo` | `email` | obrigatório, `EmailAddress()` | "O e-mail é obrigatório." / "E-mail inválido." |
| | `password` | obrigatório | "A senha é obrigatória." |

Limites de 120/254 vêm da spec (FR-003) e são mais estritos que as colunas (200/260) — ok.
Corpo JSON malformado continua caindo em `ModelStateError()` (400 com campo `body`).

## §6. Remoção completa do dev-login (backend)

**Decisão**: apagar `DevLoginInfo`, `DevLoginInfoValidator`, `IAuthService.DevLoginAsync`,
`AuthService.DevLoginAsync`, a ação `AuthController.DevLogin`, `DevLoginDocumentFilter` e
`HostEnvironmentExtensions.IsDevLoginEnabled` (o arquivo inteiro se a constante `Homolog`
não tiver outro uso). `Program.cs` perde o `DocumentFilter` e a descrição do esquema Bearer
passa a citar `POST /auth/login` e `/auth/register`. Com a ação apagada, a rota responde 404
pelo roteamento normal — indistinguível de rota inexistente (FR-012), sem código condicional
por ambiente. Em `Ambrosia.Tests`: apagar `DevLoginInfoValidatorTest` e os dois casos de
`AuthServiceTest` que chamam `DevLoginAsync` (ajuste mínimo para compilar; nenhum teste
unitário novo — constituição §VIII).

## §7. Testes de API: usuários criados pela própria suíte

**Decisão**: o `ApiTestFixture` ganha:

- `RegisterAsync(email, name, password)` → `POST /auth/register`, exige 201;
- `LoginAsync(email, password)` → `POST /auth/login`, exige 200;
- `CreateUserAsync(email, name)` = `RegisterAsync` **seguido de** `LoginAsync` (o token usado
  pelos testes vem do **login**, provando a sequência pedida pela spec), devolvendo
  `LoginResult`;
- `TestPassword` lido de `Auth:TestPassword` em `appsettings.Test.json` (padrão
  `"Senha-de-teste-123"`; não é segredo — só existe em bancos de dev/homolog) e
  `RegisterEndpoint`/`LoginEndpoint` no lugar de `DevLoginEndpoint`.

`InitializeAsync` cria A (`ana+<guid8>@exemplo.com`) e B (`bia+…`) por esse caminho. Testes
que precisavam de um usuário extra (`DevLoginAsync` em `CustomersControllerTests`/`IsolationTests`)
passam a usar `CreateUserAsync`. `AuthControllerTests` é reescrito para: cadastro OK (201,
`provider = password`, token válido em `/users/me`), e-mail duplicado (409), e-mail com caixa/
espaços diferentes (409 — normalização), senha curta (400 campo `password`), nome vazio (400),
e-mail inválido (400); login OK (200, mesmo `user.id` do cadastro), senha errada (401, mensagem
genérica), e-mail inexistente (401, **mesma** mensagem), corpo vazio (400); `POST /auth/dev-login`
→ 404; Google inválido/ausente (400, mantidos). `UnauthorizedAccessTests.ThePublicLoginRoutes…`
passa a chamar `/auth/login` com um usuário recém-criado. Execuções consecutivas não colidem
(e-mail com GUID — FR-016/SC-005).

**Alternativas**: usuário fixo semeado por SQL (dependeria de setup manual e quebraria o SC-005);
cadastro sem login em seguida (não cobriria o fluxo real pedido na US4).

## §8. Frontend: formulários, rotas e estado

**Decisão**:

- **Rotas**: `/login` (existente) e `/cadastro` (nova, pública, fora de `RequireAuth`). Ambas
  redirecionam para `state.returnTo ?? '/clientes'` se já há sessão. Os links "Criar conta"
  (em `/login`) e "Já tenho conta" (em `/cadastro`) navegam com `state: { returnTo }` para
  preservar a origem (FR-011/FR-018). A função `returnToOf` sai de `LoginPage.tsx` para
  `lib/authForm.ts` para ser compartilhada.
- **AuthContext**: `loginDev` é substituído por `loginWithPassword(email, password)` e
  `register(name, email, password)`, ambos via o `runLogin` existente (mesma sessão, mesmo
  `writeSession`, mesmo ciclo de expiração — FR-004/FR-010). `error` continua sendo a
  mensagem da API (`toUserMessage`).
- **Validação no cliente** (`lib/authForm.ts`, funções puras testadas): nome obrigatório
  (≤ 120), e-mail obrigatório e com formato básico (`/^[^\s@]+@[^\s@]+\.[^\s@]+$/`, ≤ 254),
  senha 8–128, confirmação igual. O formulário só envia quando válido (US1 cenários 4–5);
  erros de campo da API (`ApiError.erros`) são mapeados de volta ao campo pelo nome
  (`name`/`email`/`password`), e `mensagem` sem campo vira erro geral do formulário (409/401).
- **`PasswordInput`** (`components/common`): `Input` do shadcn com botão "Mostrar"/"Ocultar"
  (`aria-pressed`, `aria-label`, reutilizando `common.show`/`common.hide`), `type` alternando
  entre `password` e `text` (FR-020). Usado nos dois formulários (e na confirmação).
- **Estados**: `loading` desabilita o botão e mostra "Carregando" (`aria-live`); erro geral
  em `role="alert"`; erro de campo em `aria-describedby` do input; a senha digitada
  permanece no estado do formulário após erro (edge case). Layout `max-w-md`, inputs
  `min-h-11`, funciona a 360 px.
- **Google**: botão mantido exatamente como está (`useGoogleIdentity`), acima do formulário com
  um separador "ou"; sem `VITE_GOOGLE_CLIENT_ID` o botão desabilitado some (edge case
  "ambiente sem Google configurado" → a tela mostra só o formulário).
- **i18n**: chaves novas em `login.*` (`password`, `createAccount`, `or`, `invalidCredentials`
  não — a mensagem vem da API), `register.*` (`title`, `subtitle`, `name`, `confirmPassword`,
  `submit`, `haveAccount`), `validation.*` (mensagens de campo do cliente). `login.dev` e
  `errors.devLoginUnavailable` são removidas.
- **Testes unitários** (Vitest + RTL): `authForm.test.ts`, `PasswordInput.test.tsx`,
  `LoginPage.test.tsx` (reescrito sem `VITE_DEV_LOGIN`: submit válido chama
  `loginWithPassword` e navega para `returnTo`; 401 mostra a mensagem e mantém a senha;
  link "Criar conta" leva `returnTo`), `RegisterPage.test.tsx` (validações, 409, sucesso
  navega), `AuthContext.test.tsx` (register/loginWithPassword gravam sessão),
  `apiClient.test.ts` (401 anônimo → `ApiError`).

## §9. Remoção do dev-login no build e prova automatizada

**Decisão**: apagar `DevLoginForm.tsx`, o `lazy()` condicional de `LoginPage`, `loginDev` do
serviço/contexto, `DevLoginInfo` dos tipos, `VITE_DEV_LOGIN` de `.env.example`,
`.env.development`, `.env.homolog`, `.env.production`, `vite-env.d.ts`, `Dockerfile`
(`ARG`/`ENV`) e dos `build.args` dos dois compose. `scripts/verify-prod.mjs` mantém o nome e o
script `verify:prod`, acrescenta `VITE_DEV_LOGIN` à lista `FORBIDDEN` e o `Dockerfile` passa a
executá-lo **incondicionalmente** após o build (homolog e production) — SC-004 "em qualquer
modo". O orçamento de bundle do script é mantido.

**Alternativa**: renomear para `verify:build` — rejeitado para não quebrar `MAESTRO.md`,
READMEs e o hábito já documentado; o nome continua verdadeiro (prova do pacote).

## §10. Coleção do Bruno

**Decisão**: `bruno/Auth/Criar conta.bru` (seq 1, `POST {{baseUrl}}/auth/register` com
`userName`/`userEmail`/`userPassword`, grava `token` se 201) e `bruno/Auth/Login com senha.bru`
(seq 2, `POST /auth/login`, grava `token` se 200); `Google Login.bru` passa a seq 3;
`Dev Login.bru` apagado. `environments/Homolog.bru`: `devEmail`/`devName` → `userEmail`
(`homolog@ambrosia.test`) e `userName` (`Homologação`) em `vars`; `userPassword` entra em
`vars:secret` junto de `token` e `googleIdToken`. `collection.bru`: mesmas variáveis em
`vars:pre-request` (sem o token hard-coded expirado) e `docs` com o novo passo 2 ("Criar conta"
na primeira vez, "Login com senha" nas seguintes — o cadastro repetido responde 409 e não grava
token). `bru run --env Homolog` continua funcionando (SC-008).

## §11. Documentação e constituição

**Decisão**: remover a linha `/auth/dev-login` de `deploy/README.md` e `backend/README.md`
(tabela de `ASPNETCORE_ENVIRONMENT` e passo 3 do fluxo), `frontend/README.md` (scripts, variáveis,
build Docker), `README.md` raiz, `docs/README.md`, `docs/historias/001…`/`002…` (nota de
substituição pela 003, sem reescrever o histórico), `007`/`009` (observação de que o dev-login
não existe mais), `MAESTRO.md` (comentário do `verify:prod`) e o comentário de
`backend/scripts/seed-load-test.sql`. O contrato consolidado
`specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml` ganha `/auth/register` e
`/auth/login` e perde `/auth/dev-login`/`DevLoginInfo` (fonte do Swagger alinhado pelo
`ContractAlignmentOperationFilter`). A emenda da §VI está em
[constitution-amendment.md](./constitution-amendment.md) e é aplicada em
`.specify/memory/constitution.md` (3.2.0 → 3.3.0, MINOR: expansão do esquema + regra mais
restritiva) na aprovação do PR.

## §12. Sem migração de banco

**Decisão**: nenhuma migração EF. Verificado em `AmbrosiaContext.ConfigureUser`:
`password_hash varchar(260)` nulo, `ux_users_email` único, `provider integer`. O formato do §1
cabe no tamanho; a unicidade resolve a concorrência; o enum muda só o nome. `AmbrosiaContextModelSnapshot`
não muda (nenhuma anotação de coluna é alterada).
