# 008 — OAuth 2.1 para usar o MCP no ChatGPT

> Documento da história de usuário 008. **Incorporada à [005](005-plugin-chatgpt-mcp.md) em 27/10/2026.**

**Created:** 2026-10-25
**Last Updated:** 2026-10-27

| Campo | Valor |
|---|---|
| ID | 008 |
| Status | Incorporada à [005](005-plugin-chatgpt-mcp.md) |
| Spec | — |

---

O login por OAuth 2.1 deixou de ser uma história separada. O ChatGPT exige OAuth para usar um plugin com dados do usuário, então o MCP já nasce com o login do Ambrosio. Todo o conteúdo desta história foi para a [005 — Plugin do Ambrosio no ChatGPT](005-plugin-chatgpt-mcp.md):

| Tema | Onde ficou na 005 |
|---|---|
| Endereços canônicos (issuer, `resource`, metadata) | [Seção 2](005-plugin-chatgpt-mcp.md#endereços-canônicos-definidos-em-25102026) |
| Requisitos do ChatGPT (PKCE, PRM, CIMD, DCR, RFC 9207, `resource`) | [Seção 10](005-plugin-chatgpt-mcp.md#10-requisitos-do-chatgpt-verificados-em-27102026) |
| Critérios de aceite do OAuth | [Seção 5, "Login por OAuth 2.1"](005-plugin-chatgpt-mcp.md#login-por-oauth-21) |
| Escopos | [Seção 7](005-plugin-chatgpt-mcp.md#7-escopos-oauth) |
| Decisão do authorization server (OpenIddict) e decisões técnicas | [Seção 8](005-plugin-chatgpt-mcp.md#8-decisões-técnicas-propostas) |
| Fluxo completo | [Seção 9](005-plugin-chatgpt-mcp.md#9-fluxo-oauth-completo) |

Mudanças em relação à versão original desta história:

- O MCP não aceita mais o JWT HS256 da API nem provisoriamente: o OAuth entra junto com o servidor.
- O login no authorization server aceita Google **e** e-mail e senha (spec `003-email-password-jwt-auth`). A revisão da OpenAI exige uma conta de teste sem MFA.
- Entraram a tool de perfil (`_meta["openai/profile"]`), o `id_token_hint` e o mTLS opcional, que a documentação de plugins de outubro de 2026 descreve.
