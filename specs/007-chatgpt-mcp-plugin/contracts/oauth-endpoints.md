# Contrato: authorization server e descoberta OAuth

**Issuer**: `https://ambrosioai.site/auth` (sem barra no final). **Recurso protegido**: `https://ambrosioai.site/mcp`.
Em homologação, os dois valores vêm de configuração (`Auth:Issuer`, `Mcp:ResourceUrl`), com a URL do túnel quando o teste for pelo ChatGPT.

## 1. Descoberta

### `GET /.well-known/oauth-protected-resource/mcp` (e `GET /.well-known/oauth-protected-resource`)

Anônimo. `200 application/json`:

```json
{
  "resource": "https://ambrosioai.site/mcp",
  "authorization_servers": ["https://ambrosioai.site/auth"],
  "scopes_supported": ["openid", "email", "profile", "offline_access", "customers:read", "customers:write", "orders:read", "orders:write", "images:write", "imports:read", "imports:write"],
  "bearer_methods_supported": ["header"],
  "resource_name": "Ambrosio"
}
```

### `GET /.well-known/oauth-authorization-server/auth` e `GET /auth/.well-known/openid-configuration`

Anônimos, mesmo conteúdo. Campos obrigatórios:

```json
{
  "issuer": "https://ambrosioai.site/auth",
  "authorization_endpoint": "https://ambrosioai.site/auth/connect/authorize",
  "token_endpoint": "https://ambrosioai.site/auth/connect/token",
  "registration_endpoint": "https://ambrosioai.site/auth/connect/register",
  "userinfo_endpoint": "https://ambrosioai.site/auth/connect/userinfo",
  "revocation_endpoint": "https://ambrosioai.site/auth/connect/revoke",
  "jwks_uri": "https://ambrosioai.site/auth/.well-known/jwks",
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"],
  "token_endpoint_auth_methods_supported": ["none"],
  "client_id_metadata_document_supported": true,
  "authorization_response_iss_parameter_supported": true,
  "scopes_supported": ["openid", "email", "profile", "offline_access", "customers:read", "customers:write", "orders:read", "orders:write", "images:write", "imports:read", "imports:write"],
  "subject_types_supported": ["public"],
  "id_token_signing_alg_values_supported": ["RS256"]
}
```

### `GET /auth/.well-known/jwks`

Chave pública RSA (`kty`, `kid`, `n`, `e`, `alg: RS256`, `use: sig`). Na rotação, a chave anterior continua publicada até o último token emitido com ela expirar.

## 2. Registro do cliente

### CIMD (preferido)

`client_id` é uma URL `https` (por exemplo, `https://chatgpt.com/oauth/client.json`). No `authorize` e no `token`, o AS:

1. confere host em `Auth:Cimd:AllowedHosts` (padrão `chatgpt.com`), esquema `https` e IP público;
2. baixa o documento (*timeout* 5 s, máx. 64 KB, cache pelos headers);
3. exige `client_id` do documento igual à URL e `redirect_uri` da requisição em `redirect_uris`;
4. exige que o documento aceite `none` (cliente público com PKCE). O AS anuncia só `none`, então o ChatGPT escolhe `none` na interseção (research R20).

Falha → `400 invalid_client` (no token) ou página de erro do AS com `error=invalid_client` (no authorize).

### DCR: `POST /auth/connect/register`

Entrada `application/json` (RFC 7591): `redirect_uris` (obrigatório), `client_name?`, `token_endpoint_auth_method?` (só `none`), `grant_types?`, `response_types?`.

- Toda `redirect_uri` precisa estar em `Auth:AllowedRedirectUris`. Padrão: `https://chatgpt.com/connector_platform_oauth_redirect` e o padrão `https://chatgpt.com/connector/oauth/{callback_id}`. Em Homolog, também a URI de teste da suíte de API.
- `201` com `client_id`, `client_id_issued_at`, `token_endpoint_auth_method: "none"`, `redirect_uris`, `grant_types`. **Sem** `client_secret_expires_at` diferente de 0: o cliente não expira.
- `400 invalid_redirect_uri` ou `invalid_client_metadata` nos demais casos.

## 3. Autorização: `GET /auth/connect/authorize`

Parâmetros obrigatórios: `response_type=code`, `client_id`, `redirect_uri`, `scope`, `state`, `code_challenge`, `code_challenge_method=S256`, `resource=https://ambrosioai.site/mcp`. Opcional: `id_token_hint`.

| Situação | Resposta |
|---|---|
| Sem `code_challenge` ou com `plain` | `400 invalid_request` na própria página (validado antes do cliente; sem redirect) |
| `resource` ausente ou diferente (inclusive com barra no final) | `400 invalid_target` na própria página (idem) |
| Escopo desconhecido | Erro `invalid_scope` |
| Sem sessão no AS | Redireciona para `/auth/login?returnUrl=…` |
| Com sessão e consentimento permanente cobrindo os escopos | Emite o código direto |
| Com sessão e sem consentimento | Redireciona para `/auth/consent` |
| Sucesso | `302 {redirect_uri}?code=…&state=…&iss=https%3A%2F%2Fambrosioai.site%2Fauth` |
| Erro com `redirect_uri` válida | `302 {redirect_uri}?error=…&error_description=…&state=…&iss=…` |

Toda resposta para a `redirect_uri` traz `iss` idêntico ao issuer (RFC 9207). Erros de parâmetro detectados antes de o cliente ser validado não vão para a `redirect_uri` (research R20).

### Telas (Razor Pages, pt-BR, tema do Ambrosio)

- `GET/POST /auth/login`: formulário de e-mail e senha (mesmas regras e mesma mensagem genérica de falha do `POST /api/auth/login`) e botão "Entrar com Google" (GIS; o ID token é conferido como no `POST /api/auth/google`). Sucesso cria o cookie de sessão do AS (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/auth`, 20 min) e volta ao `returnUrl`, que precisa ser relativo e começar com `/auth/connect/authorize`.
- `GET/POST /auth/consent`: lista os escopos pedidos com `display_name` e `description` em pt-BR, separados por área e por leitura/gravação, com os botões "Autorizar" e "Negar". Negar → `error=access_denied` na `redirect_uri`. Autorizar → grava autorização permanente e emite o código. Formulários com antiforgery.

## 4. Token: `POST /auth/connect/token`

`application/x-www-form-urlencoded`.

| Grant | Parâmetros | Regras |
|---|---|---|
| `authorization_code` | `code`, `code_verifier`, `redirect_uri`, `client_id`, `resource` | PKCE S256 conferido; código de uso único; `resource` igual ao do authorize |
| `refresh_token` | `refresh_token`, `client_id`, `resource` | Rotação: devolve novo refresh e marca o antigo como usado; reuso de refresh já usado revoga a autorização inteira (`invalid_grant`) |

Resposta `200`:

```json
{
  "access_token": "<JWT>",
  "token_type": "Bearer",
  "expires_in": 1800,
  "refresh_token": "<opaco>",
  "id_token": "<JWT>",
  "scope": "openid email profile offline_access customers:read orders:read"
}
```

### Access token (JWT RS256, não criptografado)

| Claim | Valor |
|---|---|
| `iss` | `https://ambrosioai.site/auth` |
| `aud` | `https://ambrosioai.site/mcp` |
| `sub` | `user_id` em texto |
| `scope` | Escopos concedidos, separados por espaço |
| `client_id` | Cliente OAuth |
| `exp` / `nbf` / `iat` / `jti` | Validade de 30 min |

O MCP recusa (HTTP 401) assinatura inválida, `iss` diferente, `aud` diferente, expirado, ainda não válido, e o JWT HS256 das telas.

## 5. `GET /auth/connect/userinfo`

Bearer do AS. `200`: `sub`, `email`, `email_verified: true`, `name`.

## 6. `POST /auth/connect/revoke`

RFC 7009. Revoga o refresh token (e a autorização ligada). `200` sempre que a requisição for válida.

## 7. Configuração

| Chave | Exemplo | Regra |
|---|---|---|
| `Auth__Issuer` | `https://ambrosioai.site/auth` | Obrigatória, `https` fora de Development, sem barra no final |
| `Mcp__ResourceUrl` | `https://ambrosioai.site/mcp` | Idem |
| `Auth__SigningKeyPem` | PEM PKCS#8 RSA 2048 | Obrigatória em Homolog e Production (segredo) |
| `Auth__AllowedRedirectUris` | lista | Padrão com as URIs do ChatGPT |
| `Auth__Cimd__AllowedHosts` | `["chatgpt.com"]` | — |
| `Auth__AccessTokenMinutes` / `Auth__RefreshTokenDays` | 30 / 14 | — |
| `Mcp__AllowedOrigins` | lista | Origens de navegador aceitas no `/mcp` |
| `Auth__GoogleClientId` | igual ao da API | Botão do Google na tela de login |
| `ConnectionStrings__AmbrosioContext`, `CpfProtection__*`, `ObjectStorage__*` | iguais aos da API | O host reaproveita o domínio |

O startup recusa configuração incompleta, como a API (Princípio VI).
