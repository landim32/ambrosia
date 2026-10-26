<!--
Sync Impact Report
==================
Version change: 2.0.0 → 2.1.0 (MINOR)

Bump rationale: the local Docker prohibition was lifted (relaxation, no existing code breaks)
and a new mandatory skill (`wsl-docker`) was added — material expansion of guidance.

Modified principles:
- I. Skills de Arquitetura Obrigatórias: `wsl-docker` added to the mandatory skills table.
- II. Stack Tecnológica Fixa: rule forbidding `docker` / `docker compose` locally replaced by a
  rule allowing them, provided the `wsl-docker` skill is followed.

Added sections: none
Removed sections: none
Quality gates: new checklist item for `wsl-docker`.

Out-of-constitution changes (same request):
- Skill `.claude/skills/wsl-docker/` created.

Previous amendment (1.0.0 → 2.0.0): NAuth and zTools removed from the stack; Principle VI
authentication scheme set to TODO(AUTH_SCHEME); nauth-guide and ztools-guide skills deleted.

Follow-up TODOs:
- TODO(AUTH_SCHEME): define the authentication scheme that replaces NAuth (Principle VI).
- Casing divergence (deferred by decision): Principle III requires `Contexts/` and `Services/`,
  but the `react-architecture` skill generates `src/contexts/` and `src/services/`. Decide which
  one prevails.
- Resolved: duplicate skill `.claude/skills/react-arch/` deleted.
- `{NomeDoProjeto}` is intentionally kept as a convention token (not a template placeholder).
- Resolved: `dotnet-architecture` and `react-architecture` are installed in `.claude/skills/`.
-->

# Ambrosia Constitution

> Padrões obrigatórios de stack tecnológica, convenções de código, arquitetura e segurança que
> DEVEM ser seguidos por todos os contribuidores em todos os projetos.

## Core Principles

### I. Skills de Arquitetura Obrigatórias

Para implementar novas entidades e funcionalidades, as skills abaixo DEVEM ser utilizadas:

| Skill | Quando usar | Invocação |
|---|---|---|
| **dotnet-architecture** | Criar/modificar entidades, services, repositories, DTOs, migrations, DI no backend | `/dotnet-architecture` |
| **react-architecture** | Criar Types, Service, Context, Hook e registrar Provider no frontend | `/react-architecture` |
| **wsl-docker** | Executar qualquer comando `docker` / `docker compose` nesta máquina (via WSL2) | `/wsl-docker` |

Estas skills são a fonte normativa para:

- Estrutura de projetos e fluxo de dependência (Clean Architecture no backend).
- Repositórios genéricos, mapeamento manual e DI centralizado.
- Configuração de DbContext, Fluent API e migrações via `dotnet ef`.
- Padrões de arquivos frontend (Types, Services, Contexts, Hooks).
- Provider chain e registro de novos providers.
- Execução de Docker no ambiente local (comandos via `wsl`, mapeamento de caminhos, regras de
  segurança para containers de outros projetos).
- Tratamento de erros no frontend (`handleError`, `clearError`, loading state).
- Nomeação de DTOs (`Info`, `InsertInfo`, `Result`) e chaves em português
  (`sucesso`, `mensagem`, `erros`).

Esses padrões NÃO DEVEM ser reimplementados manualmente. Siga as skills.

**Rationale**: uma única fonte de padrões arquiteturais elimina divergência entre projetos e
contribuidores.

### II. Stack Tecnológica Fixa

**Backend**

| Tecnologia | Versão | Finalidade |
|---|---|---|
| .NET | 8.0 | Runtime e framework principal |
| Entity Framework Core | 9.x | ORM e migrações |
| PostgreSQL | Latest | Banco de dados relacional |
| Swashbuckle | 8.x | Swagger / OpenAPI |

**Frontend**

| Tecnologia | Versão | Finalidade |
|---|---|---|
| React | 18.x | Framework UI |
| TypeScript | 5.x | Tipagem estática |
| React Router | 6.x | Roteamento SPA |
| Vite | 6.x | Build toolchain |
| Bootstrap | 5.x | Sistema de grid e componentes base |
| i18next | 25.x | Internacionalização |
| Axios | 1.x | HTTP client (legado) |
| Fetch API | Nativo | HTTP client (novos serviços) |

**Regras de stack (inegociáveis)**

- Vite é o bundler obrigatório. NÃO usar CRA, Webpack manual ou outros bundlers.
- EF Core é o único ORM permitido. NÃO introduzir ORMs alternativos (Dapper etc.).
- Context API é o padrão de estado. NÃO adicionar Redux, Zustand, MobX ou similares.
- Novos serviços HTTP no frontend DEVEM usar Fetch API. Axios fica restrito ao código legado.
- Comandos `docker` e `docker compose` (incluindo build e criação de containers) são
  permitidos no ambiente local e DEVEM seguir a skill `wsl-docker` (o Docker Engine roda dentro
  do WSL2; não há `docker` no Windows).
- Variáveis de ambiente do frontend DEVEM usar o prefixo `VITE_`. NÃO usar `REACT_APP_`.

**Rationale**: uma stack fixa reduz a carga cognitiva, mantém as skills aplicáveis e evita
dependências redundantes.

### III. Case Sensitivity de Diretórios (Inviolável)

| Diretório | Casing | Motivo |
|---|---|---|
| `Contexts/` | Uppercase C | Compatibilidade Docker/Linux |
| `Services/` | Uppercase S | Compatibilidade Docker/Linux |
| `hooks/` | Lowercase h | Convenção React |
| `types/` | Lowercase t | Convenção TypeScript |

Todos os imports DEVEM corresponder exatamente ao casing no disco.

**Rationale**: sistemas de arquivos Linux diferenciam maiúsculas e minúsculas. Um casing
divergente compila no Windows/macOS e quebra o build em containers.

### IV. Convenções de Código

**Backend (.NET)**

| Elemento | Convenção | Exemplo |
|---|---|---|
| Namespaces | PascalCase, file-scoped | `namespace {NomeDoProjeto}.Domain.Services;` |
| Classes / Interfaces | PascalCase | `CampaignService`, `ICampaignRepository` |
| Métodos | PascalCase | `GetById()`, `MapToDto()` |
| Propriedades | PascalCase | `CampaignId`, `CreatedAt` |
| Campos privados | _camelCase | `_repository`, `_context` |
| Constantes | UPPER_CASE | `BUCKET_NAME` |

**Frontend (TypeScript/React)**

| Elemento | Convenção | Exemplo |
|---|---|---|
| Componentes | PascalCase | `LoginPage`, `CampaignCard` |
| Interfaces | PascalCase | `CampaignContextType` |
| Variáveis / Funções | camelCase | `getHeaders`, `loadCampaigns` |
| Constantes | UPPER_CASE | `AUTH_STORAGE_KEY` |
| Tipos | `interface` (não `type`) | `interface CampaignInfo {}` |
| Funções | Arrow functions | `const fn = () => {}` |
| Variáveis | `const` por padrão | `const campaigns = []` |

**JSON Property Names**

- Backend: toda propriedade de DTO DEVE ter `[JsonPropertyName("camelCase")]`.
- Frontend: os tipos TypeScript DEVEM acessar as propriedades diretamente em camelCase.

**Rationale**: nomes consistentes tornam o contrato entre backend e frontend previsível e
dispensam camadas de conversão.

### V. Convenções de Banco de Dados (PostgreSQL)

| Elemento | Convenção | Exemplo |
|---|---|---|
| Tabelas | snake_case plural | `campaigns`, `campaign_entries` |
| Colunas | snake_case | `campaign_id`, `created_at` |
| Primary Keys | `{entidade}_id`, bigint identity | `campaign_id bigint PK` |
| Constraint PK | `{tabela}_pkey` | `campaigns_pkey` |
| Foreign Keys | `fk_{pai}_{filho}` | `fk_campaign_entry` |
| Delete behavior | `ClientSetNull` | Nunca Cascade |
| Timestamps | `timestamp without time zone` | Sem timezone |
| Strings | `varchar` com MaxLength | `varchar(260)` |
| Booleans | `boolean` com default | `DEFAULT true` |
| Status/Enums | `integer` | `DEFAULT 1` |

Delete em cascata é PROIBIDO. Os detalhes de DbContext, Fluent API e migrações seguem a skill
`dotnet-architecture`.

**Rationale**: snake_case é idiomático no PostgreSQL, e `ClientSetNull` evita perda de dados em
cascata.

### VI. Autenticação e Segurança

| Aspecto | Padrão |
|---|---|
| Esquema | TODO(AUTH_SCHEME): esquema de autenticação a definir (NAuth foi removido) |
| Storage (frontend) | localStorage, chave `"login-with-metamask:auth"` |
| Proteção de rotas | Atributo `[Authorize]` nos controllers |

**Regras de segurança**

- Tokens NUNCA DEVEM ser armazenados em cookies. Usar localStorage.
- Connection strings e secrets NUNCA DEVEM ser expostos no frontend.
- Controllers com dados sensíveis DEVEM ter `[Authorize]`.
- CORS `AllowAnyOrigin` é permitido APENAS no ambiente Development.

**Rationale**: regras explícitas de armazenamento e proteção impedem vazamento de credenciais
e endpoints desprotegidos, independentemente do esquema de autenticação adotado.

## Restrições Adicionais

### Variáveis de Ambiente

**Backend**

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ConnectionStrings__{NomeDoProjeto}Context` | Sim | Connection string PostgreSQL |
| `ASPNETCORE_ENVIRONMENT` | Sim | Development, Docker, Production |

**Frontend**

| Variável | Obrigatória | Descrição |
|---|---|---|
| `VITE_API_URL` | Sim | URL base da API backend |
| `VITE_SITE_BASENAME` | Não | Base path do React Router |

O prefixo `VITE_` é obrigatório, e as variáveis DEVEM ser acessadas via `import.meta.env.VITE_*`.

### Tratamento de Erros no Backend

Os controllers DEVEM capturar exceções e retornar HTTP 500 com a mensagem:

```csharp
try { /* lógica */ }
catch (Exception ex) { return StatusCode(500, ex.Message); }
```

O tratamento de erros no frontend segue a skill `react-architecture`.

## Fluxo de Desenvolvimento e Quality Gates

Antes de submeter qualquer código, o contribuidor DEVE verificar:

- [ ] Utilizou a skill `dotnet-architecture` para novas entidades backend.
- [ ] Utilizou a skill `react-architecture` para novas entidades frontend.
- [ ] Tabelas e colunas seguem snake_case no PostgreSQL.
- [ ] Imports respeitam o casing exato dos diretórios.
- [ ] Variáveis de ambiente do frontend usam o prefixo `VITE_`.
- [ ] Controllers com dados sensíveis possuem `[Authorize]`.
- [ ] Comandos Docker locais seguiram a skill `wsl-docker`.

Code reviews DEVEM recusar alterações que não cumpram este checklist.

## Governance

- Esta constituição prevalece sobre qualquer outra prática, guia ou preferência individual.
  Em caso de conflito, vale a constituição.
- **Emendas**: toda alteração DEVE ser proposta por escrito (PR modificando este arquivo), com
  justificativa e, quando houver impacto em código existente, plano de migração. A emenda só
  entra em vigor após aprovação em review.
- **Versionamento** (SemVer):
  - MAJOR: remoção ou redefinição incompatível de princípios ou regras de governança.
  - MINOR: novo princípio/seção ou expansão material de orientação.
  - PATCH: esclarecimentos, redação e correções sem efeito semântico.
- **Compliance**: todo PR e review DEVE verificar a conformidade com os princípios acima.
  Exceções DEVEM ser justificadas explicitamente no PR. Specs e planos (`/speckit.plan`) DEVEM
  passar pelo Constitution Check antes da implementação.
- Para orientação detalhada de implementação, use as skills `dotnet-architecture`,
  `react-architecture` e `wsl-docker`.

**Version**: 2.1.0 | **Ratified**: 2026-04-02 | **Last Amended**: 2026-10-25
