# Proposta de emenda à constituição: 3.3.0 → 3.4.0 (MINOR)

**Feature**: `007-chatgpt-mcp-plugin` | **Data**: 2026-10-27 | **Princípio afetado**: VI. Autenticação e Segurança

## Motivo

O plugin do ChatGPT exige OAuth 2.1 (código de autorização com PKCE, descoberta por metadados, `resource` no `aud`, JWKS). O Princípio VI descreve um único esquema, o JWT HS256 emitido pela API, e tem três regras que o desenho da feature não cumpre sem mudança:

1. o esquema de token é só o HS256 da API;
2. "Tokens NUNCA DEVEM ser armazenados em cookies";
3. "`[AllowAnonymous]` só é permitido em `/auth/*`".

A emenda é MINOR: acrescenta um segundo esquema com escopo restrito ao MCP e esclarece duas regras, sem remover nem redefinir de forma incompatível o que existe. A API REST e o frontend continuam exatamente como estão.

## Texto proposto

### Tabela "Aspecto | Padrão": nova linha depois de "Esquema"

| Aspecto | Padrão |
|---|---|
| Esquema do MCP (plugin do ChatGPT) | OAuth 2.1 com authorization server próprio em `/auth` (issuer `https://ambrosioai.site/auth`): código de autorização com PKCE S256, consentimento por escopos, refresh token com rotação. Access token JWT RS256 com `aud` = URL do MCP (`https://ambrosioai.site/mcp`), `sub` = `user_id`, validado pelo JWKS em toda chamada. Aceito **só** no MCP; a API REST não aceita esse token e o MCP não aceita o HS256 da API. |

### Regra de cookies (substitui a atual)

- Tokens de acesso NUNCA DEVEM ser armazenados em cookies. O frontend usa localStorage. **Exceção**: as telas de login e consentimento do authorization server podem usar um cookie de sessão próprio (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/auth`, curta duração, formulários com antiforgery). Esse cookie não carrega token de API.

### Regra de rotas anônimas (substitui a atual)

- `[AllowAnonymous]` só é permitido em `/auth/*` e nos documentos de descoberta OAuth em `/.well-known/oauth-*`. No endpoint MCP, `initialize`, `tools/list` e tools declaradas `noauth` podem ser anônimas **desde que não devolvam dado de usuário**. Toda tool que acessa dados exige o token do MCP e confere os escopos antes de executar.

### Nova regra

- A chave de assinatura do authorization server (RSA, PEM) DEVE vir de variável de ambiente em Homolog e Production, e o host DEVE recusar o startup sem ela. Issuer e URL do MCP DEVEM ser configurados sem barra no final e validados no startup.

### Quality gate novo

- [ ] Tools do MCP conferem escopo antes de executar, nunca recebem `userId` como argumento e não aceitam o JWT HS256 da API.

## Impacto

- **Código existente**: nenhum no **esquema de autenticação**. A API e o frontend não mudam de esquema.
- **Exceção registrada (27/10/2026, decidida na implementação)**: `OrderInfo`/`OrderDetailInfo`
  passam a expor `capturedAt` (a coluna `captured_at` já existe na entidade `Order` e no banco desde a
  feature 006, mas nenhum DTO a mapeava). Sem isso a FR-017 ("para pedidos importados do +Vendas, a
  data da captura") não tem como ser cumprida pela tool `obter_pedido`, e o caminho escolhido manter a
  simetria "a tool chama o mesmo serviço de domínio do controller" é justamente o que acrescenta o
  campo à resposta do `GET /orders`. É adição de campo, não mudança de esquema nem de autenticação:
  nenhuma rota muda de verbo, de caminho ou de critério de acesso, e o Princípio VI continua
  satisfeito. O frontend não é afetado (não lê a propriedade).
- **Skills**: nenhuma alteração obrigatória.
- **Sync Impact Report**: atualizar o cabeçalho da constituição com a versão 3.4.0, o princípio modificado (VI) e o quality gate novo.

## Aprovação

A implementação da feature começa depois que esta emenda for aplicada a `.specify/memory/constitution.md` e aprovada em review (Governance).
