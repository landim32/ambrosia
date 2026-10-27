# Data Model — 003-email-password-jwt-auth

Phase 1. Entidades, DTOs, regras de validação e transições desta feature. O banco **não muda**
(research §12): tudo abaixo cabe no esquema da HU 001.

## 1. Usuário (`users`) — existente, semântica ampliada

| Coluna | Tipo | Alteração nesta feature |
|---|---|---|
| `user_id` | bigint identity PK | — |
| `email` | varchar(260) NOT NULL, único (`ux_users_email`) | — (continua normalizado: `Trim().ToLowerInvariant()`) |
| `name` | varchar(200) NOT NULL | — |
| `password_hash` | varchar(260) NULL | **passa a ser usado**: `pbkdf2-sha256$<iter>$<salt>$<hash>` (research §1); `NULL` = conta sem senha (Google ou usuário antigo de dev-login) |
| `provider` | integer NOT NULL | valor `2` deixa de significar `Dev` e passa a `Password` (enum renomeado, sem migração) |
| `provider_subject` | varchar(260) NULL | `sub` do Google; `NULL` em contas por senha |
| `created_at` / `updated_at` | timestamp without time zone | `updated_at` atualizado em cada login (senha ou Google) e ao limpar a senha |

**Modelo de domínio (`Ambrosio.Domain/Models/User.cs`)** — membros novos:

| Membro | Descrição |
|---|---|
| `bool HasPassword` | `PasswordHash is not null` |
| `void SetPassword(string passwordHash, DateTime now)` | define o hash (apenas no cadastro) e `UpdatedAt` |
| `void ClearPassword(DateTime now)` | `PasswordHash = null`, `UpdatedAt = now` (login Google em conta com senha — FR-009) |
| `void Touch(DateTime now)` | `UpdatedAt = now` (login com senha bem-sucedido) |
| `UpdateFromProvider(name, subject, now)` | existente; o `AuthService` passa a também fazer `Provider = Google` quando reutiliza a conta pelo Google |

**Invariantes**:

- `PasswordHash` **nunca** é mapeado para `UserInfo` nem para qualquer DTO (FR-005, constituição §VI).
- `Provider = Password` ⇒ conta criada por `/auth/register` (ou usuário antigo de dev-login, que
  tem `PasswordHash = NULL` e por isso não entra por senha).
- `Provider = Google` ⇒ `PasswordHash = NULL` sempre (garantido por `ClearPassword` no login Google).
- Unicidade do e-mail normalizado garantida pelo índice; a corrida de cadastro simultâneo é
  resolvida pelo banco (apenas uma linha; a outra requisição recebe 409).

### Transições de estado da conta

```text
                         register (e-mail novo)
      [inexistente] ─────────────────────────────▶ [Password, hash definido]
            │                                                 │
            │ google (e-mail novo)                            │ google (mesmo e-mail)
            ▼                                                 ▼  (ClearPassword, Provider=Google)
      [Google, hash NULL] ◀───────────────────────────────────┘
            │
            │ register (mesmo e-mail) → 409 "…Entre com sua conta Google."   (sem transição)
            │ login senha             → 401 genérico                          (sem transição)

      [Password, hash definido]
            │ register (mesmo e-mail) → 409 "Este e-mail já está cadastrado." (sem transição)
            │ login senha correta     → 200 (Touch)
            │ login senha errada      → 401 genérico

      [Password, hash NULL]  ← usuários antigos de dev-login (até a homolog ser reconstruída)
            │ register → 409 genérico · login senha → 401 genérico · google → vira [Google]
```

Não existe transição `[Google] → [Password]` nesta feature (troca/recuperação de senha são
histórias futuras — Assumptions da spec).

## 2. Enum `AuthProvider` (`Ambrosio.DTO/Enums`)

| Nome | Valor | JSON | Significado |
|---|---|---|---|
| `Google` | 1 | `"google"` | conta criada/reutilizada por ID token do Google |
| `Password` | 2 | `"password"` | conta criada por `/auth/register` (antes: `Dev`) |

Frontend: `UserInfo.provider: 'google' | 'password'`.

## 3. DTOs de entrada (`Ambrosio.DTO/Auth`)

### `RegisterInfo` — corpo de `POST /auth/register`

| Propriedade | JSON | Regras (validator) |
|---|---|---|
| `Name` | `name` | obrigatório; 1–120 caracteres após `Trim()` |
| `Email` | `email` | obrigatório; formato de e-mail; ≤ 254 caracteres; normalizado no serviço |
| `Password` | `password` | obrigatório; 8–128 caracteres; **sem** trim; sem regra de composição |

### `LoginInfo` — corpo de `POST /auth/login`

| Propriedade | JSON | Regras (validator) |
|---|---|---|
| `Email` | `email` | obrigatório; formato de e-mail; normalizado no serviço |
| `Password` | `password` | obrigatório (comprimento não validado: qualquer senha errada cai no 401 genérico) |

### `DevLoginInfo` — **removido**

## 4. DTO de saída — `LoginResult` (existente, inalterado)

```json
{ "token": "<jwt>", "expiresAt": "2026-10-27T03:12:00Z", "user": { "id": 1, "email": "ana@exemplo.com", "name": "Ana", "provider": "password", "createdAt": "…" } }
```

Devolvido por `/auth/register` (201), `/auth/login` (200) e `/auth/google` (200). Claims do JWT
e expiração (8 h) idênticos para as três origens (FR-004/FR-007).

## 5. Sessão (frontend, `localStorage` — inalterada)

Chave `login-with-metamask:auth` com `AuthSession { token, expiresAt, user }`. Gravada por
`runLogin` em qualquer uma das três origens; o ciclo de expiração (peek ao montar, timer,
401 em chamada autenticada) não distingue a origem (FR-010).

## 6. Exceções de domínio → HTTP

| Exceção | Origem | HTTP | `ErrorResult.mensagem` |
|---|---|---|---|
| `ValidationException` (FluentValidation) | validators | 400 | "Dados inválidos." + `erros[]` por campo |
| `DuplicateException` | cadastro com e-mail existente (serviço) ou índice único (repositório) | 409 | "Este e-mail já está cadastrado." · "Este e-mail já está cadastrado. Entre com sua conta Google." |
| `InvalidCredentialsException` (**nova**, `Ambrosio.Domain/Exceptions`) | login com senha | 401 | "E-mail ou senha inválidos" |
| `Exception` | qualquer | 500 | texto da exceção (constituição) |

## 7. Serviço de hash (`IPasswordHashAppService`, Infra.Interfaces → Infra)

| Membro | Contrato |
|---|---|
| `string Hash(string password)` | PBKDF2-HMAC-SHA256, 600.000 iterações, salt 16 B aleatório → `pbkdf2-sha256$600000$<salt>$<hash>` |
| `bool Verify(string password, string? passwordHash)` | `false` se o hash é nulo/malformado; comparação em tempo constante |
| `string DummyHash` | hash de uma senha aleatória gerada no construtor, usado para custo uniforme quando a conta não existe ou não tem senha (research §2) |

## 8. Tipos do frontend (`src/types/auth.ts`)

```ts
export interface UserInfo { id: number; email: string; name: string; provider: 'google' | 'password'; createdAt: string }
export interface LoginResult { token: string; expiresAt: string; user: UserInfo }
export interface GoogleLoginInfo { idToken: string }
export interface RegisterInfo { name: string; email: string; password: string }   // novo
export interface LoginInfo { email: string; password: string }                    // novo
export interface AuthSession { token: string; expiresAt: string; user: UserInfo }
// DevLoginInfo: removido
```

### Estado dos formulários (`lib/authForm.ts`)

| Função pura | Entrada | Saída |
|---|---|---|
| `validateLoginForm({ email, password })` | valores brutos | `{ email?: string; password?: string }` (chaves de `validation.*`) |
| `validateRegisterForm({ name, email, password, confirmPassword })` | valores brutos | `{ name?; email?; password?; confirmPassword? }` |
| `fieldErrorsOf(err)` | `ApiError` | `Record<'name'|'email'|'password', string>` a partir de `erros[].campo` |
| `returnToOf(state)` | `location.state` | `string | undefined` (movida de `LoginPage.tsx`) |

Regras do cliente espelham o §3: nome 1–120, e-mail ≤ 254 com `@` e domínio, senha 8–128,
confirmação igual à senha. Mensagens: `validation.nameRequired`, `validation.nameTooLong`,
`validation.emailRequired`, `validation.emailInvalid`, `validation.passwordLength`,
`validation.passwordMismatch`.
