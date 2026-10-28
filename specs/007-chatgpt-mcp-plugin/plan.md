# Implementation Plan: Plugin do Ambrosio no ChatGPT

**Branch**: `007-chatgpt-mcp-plugin` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/007-chatgpt-mcp-plugin/spec.md`

## Summary

Levar o Ambrosio para dentro do ChatGPT como **plugin de uso próprio** (modo desenvolvedor). Um processo novo, `Ambrosio.McpServer`, hospeda:

- o **servidor MCP** em `/mcp` (HTTP streamable, *stateless*), com uma tool para cada operação da API (clientes, pedidos, imagens, importações), as consultas prontas do doc 04, a tool de perfil e a tool `noauth`;
- o **authorization server OAuth 2.1** em `/auth` (OpenIddict), com login por e-mail e senha e pelo Google, tela de consentimento, CIMD e DCR, `resource` no `aud` e `iss` na resposta de autorização.

As tools chamam os mesmos serviços de domínio dos controllers, com o `userId` vindo do `sub` do token do AS. Toda gravação passa por prévia e confirmação e fica auditada. A paridade com a API é conferida por um teste de API que compara o Swagger com o `tools/list`. Um pacote de plugin (`plugin/`) descreve o servidor para o ChatGPT.

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (backend, novo host `Ambrosio.McpServer`); TypeScript 5.x / React 18 (frontend: só a tela de importação lê `inicio` e `fim` da URL)
**Primary Dependencies**: `ModelContextProtocol.AspNetCore` 2.x (versão fixada na implementação), OpenIddict 7.7.x (`OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`), ASP.NET Core Razor Pages (telas do AS), `Microsoft.AspNetCore.Authentication.JwtBearer` 8.x (validação no MCP), FluentValidation e serviços existentes das features 001, 003, 004 e 006
**Storage**: PostgreSQL, mesmo `AmbrosioContext`. Migração `AddOAuthServer`: `oauth_applications`, `oauth_authorizations`, `oauth_scopes`, `oauth_tokens` e `mcp_audit_entries`
**Testing**: somente testes de API (`Ambrosio.ApiTests`, skill `dotnet-test-api`) contra a homologação: fluxo OAuth completo por HTTP (DCR + PKCE + consentimento), chamadas MCP com cliente do SDK, paridade Swagger × `tools/list`, isolamento entre usuários, escopos e prévia. Frontend: teste unitário da leitura dos parâmetros na tela de importação. Verificação manual no ChatGPT pelo roteiro da spec
**Target Platform**: Linux (containers Docker): Homolog no WSL2 local, Production no servidor via GitHub Actions. ChatGPT web em modo desenvolvedor
**Project Type**: web-service (backend .NET com novo host) + pequena mudança no frontend
**Performance Goals**: consultas comuns em até 3 s para 10.000 pedidos (SC-008); conexão do plugin em menos de 2 min (SC-001)
**Constraints**: exigências do ChatGPT para plugins com dados de usuário (PKCE S256, PRM, CIMD/DCR, RFC 9207, `resource` no `aud`, `securitySchemes`, tool de perfil); endereços canônicos imutáveis (`https://ambrosiaai.site/mcp` e `/auth`, sem barra no final); nenhuma senha do +Vendas nem CPF completo em argumento, resposta ou log; listas com no máximo 200 linhas
**Scale/Scope**: uso próprio (uma vendedora, poucas conexões); 28 tools (10 de clientes e pedidos, 5 de imagens, 6 de importação, 5 curadas, perfil e `sobre_ambrosio`)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Avaliação | Situação |
|---|---|---|
| I. Skills obrigatórias | `dotnet-architecture` (entidades de auditoria e OAuth, DI), `dotnet-env` (serviço `mcp` nos ambientes e variáveis novas), `wsl-docker` (build e compose), `dotnet-test-api` (testes de API do MCP e do AS), `react-architecture` (mudança na tela de importação, se tocar em service/context) | PASS |
| II. Stack fixa | .NET 8, EF Core 9 e PostgreSQL mantidos. Dependências novas (`ModelContextProtocol.AspNetCore`, OpenIddict, Razor Pages) não estão na lista de proibidas. Razor Pages nas telas do AS foge do frontend React | **Justificado** (Complexity Tracking) |
| III. Casing | Sem diretórios novos no frontend | PASS |
| IV. Convenções | DTOs das tools com `[JsonPropertyName]` camelCase; nomes de tools e parâmetros em português (lidos pelo modelo) | PASS |
| V. Banco | Tabelas do OpenIddict e de auditoria em snake_case, chaves `bigint` `{entidade}_id`, timestamps sem fuso, sem cascata (R15) | PASS (com ação de mapear o OpenIddict) |
| VI. Autenticação | O MCP usa um esquema novo: JWT RS256 emitido pelo AS, com `aud` = URL do MCP. O AS usa cookie de sessão `HttpOnly` nas telas de login. Há rotas anônimas fora de `/auth/*` (`/.well-known/oauth-*`, `initialize`/`tools/list` e `sobre_ambrosio` no `/mcp`). `sub` como única fonte do usuário e leituras filtradas por `userId` continuam valendo | **Violação: exige emenda** ([constitution-amendment.md](constitution-amendment.md)) |
| VII. Ambientes | Serviço `mcp` no `docker-compose.yml` (Homolog) e no `docker-compose-prod.yml`, rotas no gateway, variáveis em `.env.example`/`.env.prod.example`. Fim do ciclo com homologação reconstruída e testes de API passando | PASS |
| VIII. Testes | Backend testado só por testes de API (inclusive a paridade, R12). Frontend com teste unitário. Nada de E2E/Playwright | PASS |

**Resultado do gate**: segue para a Fase 0 com a emenda do Princípio VI proposta junto com a feature e as justificativas abaixo. A implementação só começa depois da emenda aprovada.

**Re-check pós-design (Fase 1)**: o desenho não trouxe violação nova. Modelo de dados, contratos e quickstart seguem o Princípio V (R15), o VII (serviço `mcp` na homologação e testes de API no fim do ciclo) e o VIII (paridade como teste de API). Continua pendente só a emenda do Princípio VI.

## Project Structure

### Documentation (this feature)

```text
specs/007-chatgpt-mcp-plugin/
├── plan.md                    # Este arquivo
├── research.md                # Fase 0: decisões técnicas (R1–R18)
├── data-model.md              # Fase 1: entidades novas e reaproveitadas
├── quickstart.md              # Fase 1: como subir, conectar e verificar
├── constitution-amendment.md  # Proposta de emenda ao Princípio VI
├── contracts/
│   ├── mcp-tools.md           # Contrato das 28 tools (nome, endpoint, escopos, anotações, entrada/saída)
│   ├── oauth-endpoints.md     # Authorization server, metadata e desafios
│   └── plugin-package.md      # plugin.json, mcp.json e skill de vendas
├── checklists/requirements.md
└── tasks.md                   # Fase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── Ambrosio.McpServer/                  # NOVO host ASP.NET Core (MCP + authorization server)
│   ├── Program.cs                       # Validação de configuração, OpenIddict, JwtBearer + AddMcp, MapMcp("/mcp"), Razor Pages
│   ├── Auth/
│   │   ├── Handlers/                    # CIMD, resource (RFC 8707), iss na resposta (RFC 9207)
│   │   ├── Endpoints/                   # authorize, token, userinfo, register (DCR), metadata
│   │   └── Pages/                       # Login.cshtml, Consent.cshtml (+ CSS com os tokens do Ambrosio)
│   ├── Mcp/
│   │   ├── Tools/                       # CustomerTools, OrderTools, ImageTools, ImportTools, InsightTools, ProfileTools
│   │   ├── Filters/                     # Escopo + desafio (_meta mcp/www_authenticate), auditoria, prévia
│   │   ├── Results/                     # Formatação: structuredContent + texto pt-BR, consultado_em, limite de 200
│   │   └── OriginValidationMiddleware.cs
│   └── appsettings*.json                # Auth:*, Mcp:* por ambiente
├── Ambrosio.Domain/                     # Consultas novas: clientes inativos, resumo do período; serviço de auditoria; prévia
├── Ambrosio.DTO/                        # DTOs das tools e das consultas novas
├── Ambrosio.Infra/                      # Entidades OpenIddict (long) e McpAuditEntry no AmbrosioContext; migração AddOAuthServer
├── Ambrosio.Infra.Interfaces/           # Repositório de auditoria e métodos novos de consulta
├── Ambrosio.Application/                # DI compartilhado (reaproveitado pelo McpServer)
├── Ambrosio.ApiTests/                   # McpServer/: OAuth, MCP, paridade, isolamento, escopos, prévia
└── Dockerfile                           # Novo target "mcp"

frontend/src/pages/ImportPage.tsx        # Lê ?inicio=&fim= para preencher o período (R9)

plugin/                                  # NOVO pacote do plugin
├── plugin.json
├── mcp.json
├── skills/vendas-ambrosio/SKILL.md
└── assets/                              # logo e mascote

deploy/nginx/routes.inc                  # /auth/, /mcp e /.well-known/oauth-* → serviço mcp
docker-compose.yml, docker-compose-prod.yml, .env.example, .env.prod.example
```

**Structure Decision**: aplicação web existente (backend .NET em Clean Architecture + frontend React), acrescida de **um** host novo, `Ambrosio.McpServer`, que referencia `Application`, `Domain`, `DTO`, `Infra` e `Infra.Interfaces` como a API faz. Tools e authorization server ficam juntos nesse host (R8); separar depois em `Ambrosio.Mcp` e `Ambrosio.Auth` é mecânico. A pasta `plugin/` na raiz guarda o pacote do plugin, fora do build .NET.

## Complexity Tracking

| Violação | Por que é necessária | Alternativa mais simples descartada porque |
|---|---|---|
| Segundo esquema de token (JWT RS256 do AS com `aud` = MCP) além do HS256 da API (Princípio VI) | O ChatGPT só conecta plugins com OAuth 2.1 (PKCE, PRM, `resource` no `aud`, JWKS). O HS256 da API não tem descoberta nem JWKS | Reutilizar o HS256 da API no MCP: o ChatGPT não aceita token colado nem API key, e a especificação MCP proíbe repassar o token recebido |
| Cookie de sessão `HttpOnly` nas telas do AS (Princípio VI proíbe tokens em cookies) | O AS precisa lembrar quem entrou entre o `authorize`, o login e o consentimento, na mesma origem e sem SPA. O cookie não guarda token de API: guarda só a sessão do AS, com `Path=/auth`, curta duração e antiforgery | Guardar estado no `localStorage` exigiria uma SPA no meio do fluxo OAuth e exporia o código de autorização ao JavaScript |
| Rotas anônimas fora de `/auth/*`: `/.well-known/oauth-*`, `initialize`/`tools/list` e a tool `noauth` no `/mcp` (Princípio VI) | A descoberta OAuth é anônima por definição (RFC 9728 e RFC 8414), e o ChatGPT lista as tools antes de conectar a conta. Nenhuma dessas rotas devolve dado de usuário | Exigir token em tudo impede a descoberta e a tool `sobre_ambrosio`. Sem a tool `noauth`, só os `/.well-known` continuam anônimos |
| Telas do AS em Razor Pages, fora do frontend React (Princípio II) | São duas telas server-side do fluxo OAuth, com cookie de sessão e antiforgery, servidas em `/auth` | Montar as telas no React exigiria expor o fluxo OAuth ao SPA e um segundo login no frontend só para o AS |
