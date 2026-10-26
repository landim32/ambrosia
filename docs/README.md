# Documentação do Ambrosia

> Índice da documentação: análises e arquitetura, e histórias de usuário.

**Created:** 2026-10-25
**Last Updated:** 2026-10-25

---

## Análises e arquitetura

| Doc | Descrição |
|---|---|
| [01 — Análise do sistema +Vendas](01-analise-sistema-mais-vendas.md) | Levantamento somente leitura do +Vendas (Somalabs): telas, rotas, dados disponíveis e pontos de atenção |
| [02 — Modelo de dados](02-modelo-de-dados.md) | Modelo PostgreSQL proposto, que espelha o +Vendas (fase 1) e prepara o foco em clientes (fase 2) |
| [03 — Bot Playwright](03-bot-playwright.md) | Bot de coleta somente leitura que extrai vendas e clientes do +Vendas para a base, com regras de segurança |
| [04 — MCP para consultas com LLM](04-mcp-consultas-llm.md) | Servidor MCP somente leitura sobre a base, com as tools sugeridas para consultar vendas e clientes |

---

## Histórias de usuário

| ID | História | Status | Spec |
|---|---|---|---|
| [001](historias/001-api-cadastro-clientes-pedidos.md) | [API de cadastro e consulta de clientes e pedidos](historias/001-api-cadastro-clientes-pedidos.md) | Implementada | [`specs/001-api-cadastro-clientes-pedidos/`](../specs/001-api-cadastro-clientes-pedidos/) |
| [002](historias/002-frontend-web-perfil-cliente-pedidos.md) | [Frontend web: perfil do cliente e pedidos (somente exibição)](historias/002-frontend-web-perfil-cliente-pedidos.md) | Planejada | — |
| [003](historias/003-armazenamento-imagens-s3.md) | [Armazenamento de imagens em storage compatível com S3](historias/003-armazenamento-imagens-s3.md) | Planejada | — |
| [004](historias/004-frontend-pwa.md) | [Frontend como PWA](historias/004-frontend-pwa.md) | Planejada | — |
| [005](historias/005-graphql-clientes-pedidos.md) | [GraphQL para consultas de clientes e pedidos](historias/005-graphql-clientes-pedidos.md) | Planejada | — |
| [006](historias/006-mcp-servidor-completo.md) | [Servidor MCP completo (REST e GraphQL como tools)](historias/006-mcp-servidor-completo.md) | Planejada | — |
| [007](historias/007-oauth-mcp-chatgpt.md) | [OAuth 2.1 para usar o MCP no ChatGPT](historias/007-oauth-mcp-chatgpt.md) | Planejada | — |
| [008](historias/008-mcp-app-chatgpt.md) | [MCP do Ambrosia como app no ChatGPT](historias/008-mcp-app-chatgpt.md) | Planejada | — |
| [009](historias/009-bot-migracao-mais-vendas.md) | [Bot de migração idempotente do +Vendas para o Ambrosia](historias/009-bot-migracao-mais-vendas.md) | Planejada | — |
| [010](historias/010-banco-imagens-clientes-chatgpt.md) | [Banco de imagens de clientes alimentado pelo ChatGPT](historias/010-banco-imagens-clientes-chatgpt.md) | Planejada | — |

### Endereços de produção

Definidos em 25/10/2026. Tudo fica no mesmo domínio, em subpastas, com cada serviço como processo próprio atrás do nginx ([002, roteamento](historias/002-frontend-web-perfil-cliente-pedidos.md#roteamento-em-produção)).

| Endereço | Serviço | HUs |
|---|---|---|
| `https://ambrosiaai.site` | Frontend web e PWA | [002](historias/002-frontend-web-perfil-cliente-pedidos.md), [004](historias/004-frontend-pwa.md) |
| `https://ambrosiaai.site/api` | API REST (inclui o login da 001 em `/api/auth/*`) e GraphQL em `/api/graphql` | [001](historias/001-api-cadastro-clientes-pedidos.md), [005](historias/005-graphql-clientes-pedidos.md) |
| `https://ambrosiaai.site/auth` | Authorization server OAuth 2.1 (OpenIddict). O issuer é exatamente esse valor | [007](historias/007-oauth-mcp-chatgpt.md) |
| `https://ambrosiaai.site/mcp` | Servidor MCP. O `resource` canônico é exatamente esse valor | [006](historias/006-mcp-servidor-completo.md), [007](historias/007-oauth-mcp-chatgpt.md), [008](historias/008-mcp-app-chatgpt.md), [010](historias/010-banco-imagens-clientes-chatgpt.md) |
| `https://ambrosiaai.site/.well-known/*` | Descoberta OAuth, roteada pelo nginx para o AS ou para o MCP | [007](historias/007-oauth-mcp-chatgpt.md) |

Frontend e API ficam na mesma origem, então não há CORS em produção. O storage de imagens ([003](historias/003-armazenamento-imagens-s3.md)) usa o endpoint do próprio provedor, sem domínio próprio. Se a descoberta OAuth com caminho falhar nos testes do ChatGPT, a 007 prevê o uso de subdomínios `auth.` e `mcp.`.

A stack (Tailwind CSS 4.x e shadcn/ui no frontend, HotChocolate 14.x no backend) segue a constituição 3.0.0.

### Dependências entre histórias

Seta contínua: dependência obrigatória. Seta tracejada: uso opcional ou só para exibição.

```mermaid
graph LR
    H001[001 API REST]
    H002[002 Frontend web]
    H003[003 Imagens S3]
    H004[004 PWA]
    H005[005 GraphQL]
    H006[006 MCP completo]
    H007[007 OAuth 2.1]
    H008[008 App no ChatGPT]
    H009[009 Bot de migração]
    H010[010 Fotos via ChatGPT]

    H001 --> H002
    H001 --> H003
    H001 --> H005
    H001 --> H009
    H002 --> H004
    H003 -.-> H004
    H005 --> H006
    H006 --> H007
    H006 --> H008
    H007 --> H008
    H003 --> H010
    H006 --> H010
    H007 --> H010
    H008 --> H010
    H002 -.->|exibe as fotos| H010
    H007 -.->|token do bot| H009
```

### Ordem sugerida de implementação

| Ordem | HU | Motivo |
|---|---|---|
| 1 | [009](historias/009-bot-migracao-mais-vendas.md) | Traz dados reais para a base. Valida o modelo da 001 e alimenta todas as outras HUs. Exige só o endpoint de importação |
| 2 | [002](historias/002-frontend-web-perfil-cliente-pedidos.md) | Primeira forma de consultar sem Swagger, já com dados reais |
| 3 | [005](historias/005-graphql-clientes-pedidos.md) | Base das consultas flexíveis do MCP |
| 4 | [003](historias/003-armazenamento-imagens-s3.md) | Infraestrutura independente, pode correr em paralelo com a 005 |
| 5 | [004](historias/004-frontend-pwa.md) | Evolução da 002. Fica melhor depois da 003 (cache de imagens) |
| 6 | [006](historias/006-mcp-servidor-completo.md) | Depende da 005 |
| 7 | [007](historias/007-oauth-mcp-chatgpt.md) | Depende da 006 (resource server) |
| 8 | [008](historias/008-mcp-app-chatgpt.md) | Depende da 006 e da 007 |
| 9 | [010](historias/010-banco-imagens-clientes-chatgpt.md) | Depende da 003, 006, 007 e 008 |

Se o bot depender do token da 007 (pergunta em aberto da 009), ele pode começar em Development com o `dev-login` e trocar a autenticação depois.

### Como incluir uma nova história

1. Use o próximo ID livre com 3 dígitos (a próxima é **011**), o mesmo da pasta em `specs/NNN-<slug>/`.
2. Crie `docs/historias/NNN-<slug>.md` com o mesmo `<slug>` da spec, usando o [doc 001](historias/001-api-cadastro-clientes-pedidos.md) como modelo de seções.
3. Acrescente uma linha na tabela acima. Status sugeridos: `Planejada`, `Em andamento`, `Implementada`.
