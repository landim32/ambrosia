# Proposta de emenda da constituição — 2.0.0 → 2.1.0 (MINOR)

**Origem**: feature `001-api-cadastro-clientes-pedidos` (T100)
**Status**: incorporada na constituição **3.0.0** (2026-10-26), junto com a troca de Bootstrap por Tailwind e a inclusão do HotChocolate. Única alteração em relação ao texto abaixo: a regra 6 passou a permitir `/auth/dev-login` em Development **e Homolog** (ambiente docker compose local).

## Motivo

A constituição 2.0.0 removeu o NAuth e deixou o esquema de autenticação do Princípio VI como `TODO(AUTH_SCHEME)`. A feature 001 implementou o esquema (research §1, §3, §15) e o validou com 79 testes HTTP (SC-004, SC-002). A emenda registra a decisão como padrão obrigatório.

Bump **MINOR**: resolve um TODO e acrescenta orientação, sem remover nem alterar princípio existente.

## Texto proposto — Princípio VI (Segurança e Autenticação)

Substituir o `TODO(AUTH_SCHEME)` por:

> **Autenticação.** As APIs usam **JWT Bearer emitido pela própria API** após login OAuth/OIDC (Google). O cliente obtém um ID token do provedor e o troca em `POST /auth/google` por um JWT assinado (HS256, claims `sub` = `user_id`, `email`, `name`; `iss`/`aud` configuráveis; expiração padrão de 8 h).
>
> 1. Todo controller de dados DEVE ter `[Authorize]`. `[AllowAnonymous]` só é permitido em `/auth/*`.
> 2. O identificador do usuário DEVE vir exclusivamente do claim `sub`. Nenhum DTO de entrada pode carregar `userId`.
> 3. Todo método de leitura de repositório DEVE receber `userId` e filtrá-lo como primeira cláusula. Recurso de outro usuário responde 404, indistinguível de inexistente.
> 4. O segredo JWT (mínimo 32 bytes), o pepper e a chave de cifra DEVEM vir de variáveis de ambiente; a API DEVE recusar o startup com configuração incompleta.
> 5. Senhas e hashes de senha NUNCA são mapeados para DTO. Dados pessoais sensíveis (ex.: CPF) DEVEM ser gravados como hash com pepper (unicidade) e cifra autenticada (recuperação), exibindo-se mascarados em listagens.
> 6. `POST /auth/dev-login` (token sem provedor) é permitido **somente** com `ASPNETCORE_ENVIRONMENT=Development`: responde 404 e é omitido do Swagger nos demais ambientes. CORS `AllowAnyOrigin` também só em Development.

## Itens do Sync Impact Report a atualizar

- Version change: `2.0.0 → 2.1.0`.
- Remover o `TODO(AUTH_SCHEME)` dos Follow-up TODOs.
- Quality gates: acrescentar "controllers com `[Authorize]`; leituras de repositório filtram por `userId`".

## Evidência na feature 001

| Regra | Onde está provada |
|---|---|
| 1, 2 | Auditoria T091; `UnauthorizedAccessTests` (T089) |
| 3 | `RepositoryIsolationContractTest` (T087); `IsolationTests` (T088); quickstart Cenário 4 |
| 4 | `ValidateSettings` em `Program.cs` |
| 5 | `UserServiceTest`, `UsersControllerTests`; verificação de CPF em claro = 0 no banco (T070) |
| 6 | Revisão T097: `/auth/dev-login` → 404 em Production e ausente de `paths` do Swagger |

## Pendência de governança

Divergência de caixa já registrada na constituição (`Contexts/`, `Services/` × `src/contexts/`, `src/services/` da skill `react-architecture`) permanece em aberto e fora desta emenda.
