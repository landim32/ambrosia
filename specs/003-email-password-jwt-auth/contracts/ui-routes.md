# Contrato — rotas e telas do frontend (delta sobre `specs/002-frontend-web-clientes-pedidos/contracts/ui-routes.md`)

## Rotas

| Rota | Pública | Tela | Alteração |
|---|---|---|---|
| `/login` | sim | `LoginPage` | formulário de e-mail/senha + link "Criar conta" + botão Google; **sem** formulário de desenvolvimento |
| `/cadastro` | sim | `RegisterPage` | **nova**: nome, e-mail, senha, confirmação, link "Já tenho conta" |
| demais | não | — | inalteradas (`RequireAuth` → `/login` com `state.returnTo`) |

Regras comuns às duas rotas públicas:

- Com sessão válida, `<Navigate to={returnTo ?? '/clientes'} replace />` (edge case "já conectada abre `/cadastro`").
- `returnTo` vem de `location.state.returnTo` (gravado por `RequireAuth` e por `logout('expirada')`); os links entre `/login` e `/cadastro` repassam `state: { returnTo }` para preservar o destino (FR-011/FR-018).
- `/login?motivo=expirada` continua mostrando "Sua sessão expirou".
- Nenhuma rota usa os prefixos reservados do gateway (`/api`, `/auth`, `/mcp`, `/.well-known`).

## `LoginPage` (`/login`)

```text
┌─────────────────────────────────────┐
│ Entrar no Ambrosia                  │  h1 (login.title)
│ Consulte clientes e pedidos…        │  p  (login.subtitle)
│ [alert: Sua sessão expirou]         │  só com motivo=expirada / expiryNotice
│ [ Entrar com Google ]               │  GIS (some se VITE_GOOGLE_CLIENT_ID vazio)
│ ───────────── ou ─────────────      │  login.or (só quando o botão Google existe)
│ [alert: E-mail ou senha inválidos]  │  erro geral (error do AuthContext), role="alert"
│ E-mail      [__________________]    │  type=email, autoComplete=username, aria-describedby
│ Senha       [__________] [Mostrar]  │  PasswordInput, autoComplete=current-password
│ [        Entrar        ]            │  disabled enquanto loading
│ Carregando                          │  aria-live="polite" (min-h-5)
│ Não tem conta? Criar conta          │  Link → /cadastro com state.returnTo
└─────────────────────────────────────┘
```

Comportamento:

1. Validação no cliente (`validateLoginForm`) antes do envio; campo inválido recebe a mensagem e foco.
2. `loginWithPassword(email.trim(), password)` — a senha vai **como digitada**.
3. Sucesso → sessão gravada → render seguinte cai no `<Navigate>` para `returnTo ?? '/clientes'`.
4. 401/rede → erro geral; **a senha permanece no campo**; `clearError()` ao editar qualquer campo.
5. Google: inalterado (`handleCredential` → `loginWithGoogle`).

## `RegisterPage` (`/cadastro`)

```text
┌─────────────────────────────────────┐
│ Criar conta                         │  h1 (register.title)
│ Use seu e-mail e uma senha…         │  p  (register.subtitle)
│ [alert: Este e-mail já está…]       │  erro geral (409 / rede), role="alert"
│ Nome              [______________]  │  autoComplete=name
│ E-mail            [______________]  │  autoComplete=email
│ Senha             [______] [Mostrar]│  autoComplete=new-password
│ Confirmar senha   [______] [Mostrar]│  autoComplete=new-password
│ [        Criar conta        ]       │  disabled enquanto loading
│ Carregando                          │  aria-live="polite"
│ Já tenho conta                      │  Link → /login com state.returnTo
└─────────────────────────────────────┘
```

Comportamento:

1. `validateRegisterForm` no cliente: nome 1–120, e-mail válido ≤ 254, senha 8–128, confirmação igual (US1 cenários 4–5). Só envia quando válido.
2. `register(name.trim(), email.trim(), password)` → `POST /auth/register`.
3. 201 → sessão iniciada imediatamente → `<Navigate>` para `returnTo ?? '/clientes'` (US1 cenário 1 e 7).
4. 409 → mensagem da API como erro geral (genérica ou "…Entre com sua conta Google."); 400 → `erros[]` mapeados por `campo`; rede → "Não foi possível conectar". Senhas digitadas são mantidas.

## Componente `PasswordInput` (`components/common/PasswordInput.tsx`)

| Prop | Tipo | Descrição |
|---|---|---|
| `id`, `name`, `value`, `onChange`, `autoComplete`, `aria-describedby`, `aria-invalid` | como `Input` | repassados ao `Input` do shadcn |
| `toggleLabels` | `{ show: string; hide: string }` | textos do botão (`common.show`/`common.hide`) |

Botão `type="button"` com `aria-pressed` e `aria-label` que alterna `type` entre `password` e `text` (FR-020); `min-h-11` em ambos.

## Chaves i18n novas/removidas (`src/i18n/locales/pt-BR.ts`)

| Chave | Texto |
|---|---|
| `login.password` | Senha |
| `login.or` | ou |
| `login.noAccount` | Não tem conta? |
| `login.createAccount` | Criar conta |
| `register.title` | Criar conta |
| `register.subtitle` | Use seu e-mail e uma senha para acessar o Ambrosia. |
| `register.name` | Nome |
| `register.confirmPassword` | Confirmar senha |
| `register.submit` | Criar conta |
| `register.haveAccount` | Já tenho conta |
| `validation.nameRequired` | Informe seu nome |
| `validation.nameTooLong` | O nome deve ter no máximo 120 caracteres |
| `validation.emailRequired` | Informe seu e-mail |
| `validation.emailInvalid` | E-mail inválido |
| `validation.emailTooLong` | O e-mail deve ter no máximo 254 caracteres |
| `validation.passwordLength` | A senha deve ter entre 8 e 128 caracteres |
| `validation.passwordMismatch` | As senhas não conferem |
| `common.showPassword` / `common.hidePassword` | Mostrar senha / Ocultar senha |
| ~~`login.dev`~~, ~~`login.name`~~ (vai para `register.name`), ~~`errors.devLoginUnavailable`~~ | removidas |

`login.email`, `login.submit`, `login.google`, `login.expired`, `login.signInLabel`,
`login.signOutLabel`, `errors.network`, `errors.unexpected` permanecem.

## `AuthContextType` (delta)

```ts
loginWithGoogle: (idToken: string) => Promise<boolean>                       // mantido
loginWithPassword: (email: string, password: string) => Promise<boolean>     // novo
register: (name: string, email: string, password: string) => Promise<boolean> // novo
// loginDev: removido
```
