<!--
Sync Impact Report
==================
Version change: 3.2.0 → 3.3.0 (MINOR)

Bump rationale: Princípio VI passa a descrever cadastro/login com e-mail e senha ao lado do
Google e PROÍBE emissão de token sem credencial em qualquer ambiente (o dev-login, antes
permitido em Development/Homolog, foi removido pela feature 003). Princípio VII esclarece
que os testes de API criam seus próprios usuários. Novo quality gate correspondente.

Modified principles:
- VI. Autenticação e Segurança: linha "Esquema" reescrita (três rotas públicas devolvendo o
  mesmo `LoginResult`); regra do dev-login substituída pela proibição de token sem credencial;
  nova regra de hash de senha e de resposta uniforme em falha de login; rationale ampliado.
- VII. Ambientes e Testes de API: esclarecimento — os testes de API criam seus próprios
  usuários por cadastro + login, sem usuário pré-existente nem atalho sem senha.

Added sections: none
Removed sections: none
Quality gates: added "nenhuma rota/flag de emissão de token sem credencial; testes de API e
coleções obtêm o token por cadastro + login".
Templates/skills afetados: nenhum (`dotnet-test-api` já usa fixture com login real).
Follow-up: histórias 007/009 (`docs/historias`) mencionavam o dev-login como possível
bootstrap do bot/MCP — devem usar cadastro + login ou token próprio.

Previous amendment (3.1.0 → 3.2.0, MINOR): new principle VIII (Estratégia de Testes) defining the
project's testing policy — backend only through API tests, frontend only through unit tests, with
Playwright and any browser E2E testing prohibited; matching stack rule added to Principle II. No
existing principle was removed and no code was affected (no E2E/Playwright existed). Quality gate
added: "frontend testado apenas com testes unitários (sem Playwright/E2E)".

Previous amendment (3.0.0 → 3.1.0, MINOR): new principle VII (environments and API tests): the
Development / Homolog / Production environments are created with the `dotnet-env` skill, Homolog
runs on local Docker (docker compose in WSL), and every development cycle ends with the API tests
running against Homolog. `dotnet-env` and `dotnet-test-api` added to the mandatory skills table.

Previous amendment (2.1.0 → 3.0.0, MAJOR). Bump rationale: Bootstrap 5 was removed from the fixed
frontend stack and replaced by Tailwind CSS + shadcn/ui (incompatible redefinition of a stack
rule; no Bootstrap code exists yet). HotChocolate was added to the backend stack for GraphQL, with
the `dotnet-graphql` skill made mandatory.

Modified principles (3.0.0):
- I. Skills de Arquitetura Obrigatórias: `dotnet-graphql` added to the mandatory skills table.
- II. Stack Tecnológica Fixa: Bootstrap 5.x → Tailwind CSS 4.x + shadcn/ui; HotChocolate 14.x added;
  new rules: GraphQL only via HotChocolate, styling only via Tailwind (no Bootstrap or other CSS
  frameworks).
- VI. Autenticação e Segurança: TODO(AUTH_SCHEME) resolved with the scheme implemented in feature
  001 (proposal specs/001-api-cadastro-clientes-pedidos/constitution-amendment.md, folded into
  this amendment); `/auth/dev-login` allowed in Development and Homolog.
- Environment variables: `ASPNETCORE_ENVIRONMENT` values renamed to Development / Homolog /
  Production (the Docker environment became Homolog).
Quality gates (3.0.0): added "leituras de repositório filtram por `userId`".

Previous amendment (2.1.0): local Docker prohibition lifted; `wsl-docker` skill added.

Previous amendment (2.0.0): NAuth and zTools removed from the stack; Principle VI
authentication scheme set to TODO(AUTH_SCHEME); nauth-guide and ztools-guide skills deleted.

Follow-up TODOs:
- Casing divergence (deferred by decision): Principle III requires `Contexts/` and `Services/`,
  but the `react-architecture` skill generates `src/contexts/` and `src/services/`. Decide which
  one prevails.
- Verificar se o repositório já contém testes E2E/Playwright no frontend (dependências,
  configuração, pastas) e removê-los em uma tarefa separada.
- `{NomeDoProjeto}` is intentionally kept as a convention token (not a template placeholder).
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
| **dotnet-graphql** | Criar/modificar schema, queries e type extensions GraphQL (HotChocolate) | `/dotnet-graphql` |
| **wsl-docker** | Executar qualquer comando `docker` / `docker compose` nesta máquina (via WSL2) | `/wsl-docker` |
| **dotnet-env** | Criar/modificar os ambientes Development, Homolog e Production (appsettings, docker compose, `.env`, pipeline de deploy) | `/dotnet-env` |
| **dotnet-test-api** | Criar/modificar os testes de API (`{NomeDoProjeto}.ApiTests`) | `/dotnet-test-api` |

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
| HotChocolate | 14.x | GraphQL (consultas) |

**Frontend**

| Tecnologia | Versão | Finalidade |
|---|---|---|
| React | 18.x | Framework UI |
| TypeScript | 5.x | Tipagem estática |
| React Router | 6.x | Roteamento SPA |
| Vite | 6.x | Build toolchain |
| Tailwind CSS | 4.x | Estilização utility-first |
| shadcn/ui | Latest | Componentes base (Radix UI + Tailwind) |
| i18next | 25.x | Internacionalização |
| Axios | 1.x | HTTP client (legado) |
| Fetch API | Nativo | HTTP client (novos serviços) |

**Regras de stack (inegociáveis)**

- Vite é o bundler obrigatório. NÃO usar CRA, Webpack manual ou outros bundlers.
- Tailwind CSS é o único framework de estilo. NÃO usar Bootstrap, Material UI ou similares;
  componentes base vêm do shadcn/ui.
- HotChocolate é a única biblioteca GraphQL permitida e DEVE seguir a skill `dotnet-graphql`.
- EF Core é o único ORM permitido. NÃO introduzir ORMs alternativos (Dapper etc.).
- Context API é o padrão de estado. NÃO adicionar Redux, Zustand, MobX ou similares.
- Novos serviços HTTP no frontend DEVEM usar Fetch API. Axios fica restrito ao código legado.
- Comandos `docker` e `docker compose` (incluindo build e criação de containers) são
  permitidos no ambiente local e DEVEM seguir a skill `wsl-docker` (o Docker Engine roda dentro
  do WSL2; não há `docker` no Windows).
- Variáveis de ambiente do frontend DEVEM usar o prefixo `VITE_`. NÃO usar `REACT_APP_`.
- NÃO usar Playwright, Cypress, Selenium ou qualquer outra ferramenta de teste E2E/de navegador
  no frontend (ver Princípio VIII).

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
| Esquema | JWT Bearer HS256 emitido pela própria API (`sub` = `user_id`, `email`, `name`; `iss`/`aud` configuráveis; expiração padrão de 8 h), obtido por **uma** das rotas públicas: `POST /auth/register` (cadastro com nome, e-mail e senha — já devolve a sessão), `POST /auth/login` (e-mail e senha) ou `POST /auth/google` (troca do ID token do Google). As três devolvem o mesmo `LoginResult`. |
| Storage (frontend) | localStorage, chave `"login-with-metamask:auth"` |
| Proteção de rotas | Atributo `[Authorize]` nos controllers |

**Regras de segurança**

- Tokens NUNCA DEVEM ser armazenados em cookies. Usar localStorage.
- Connection strings e secrets NUNCA DEVEM ser expostos no frontend.
- Controllers com dados sensíveis DEVEM ter `[Authorize]`.
- CORS `AllowAnyOrigin` é permitido APENAS no ambiente Development.
- `[AllowAnonymous]` só é permitido em `/auth/*`.
- O identificador do usuário DEVE vir exclusivamente do claim `sub`. Nenhum DTO de entrada pode
  carregar `userId`.
- Todo método de leitura de repositório DEVE receber `userId` e filtrá-lo como primeira cláusula.
  Recurso de outro usuário responde 404, indistinguível de inexistente.
- O segredo JWT (mínimo 32 bytes), o pepper e a chave de cifra DEVEM vir de variáveis de ambiente
  (ou do `appsettings.Development.json` local); a API DEVE recusar o startup com configuração
  incompleta.
- Senhas e hashes de senha NUNCA são mapeados para DTO. Dados pessoais sensíveis (ex.: CPF) DEVEM
  ser gravados como hash com pepper (unicidade) e cifra autenticada (recuperação), exibidos
  mascarados em listagens.
- É PROIBIDA qualquer rota, flag de build ou atalho que emita token sem credencial
  (ex.: o antigo `POST /auth/dev-login`), em QUALQUER ambiente, inclusive Development e
  Homolog. Testes de API, coleções HTTP e quickstarts DEVEM obter o token por
  `POST /auth/register` + `POST /auth/login` com usuários criados pela própria execução.
- Senhas DEVEM ser armazenadas apenas como hash com salt aleatório por senha, com algoritmo
  de derivação lenta (PBKDF2-HMAC-SHA256 ≥ 600.000 iterações ou mais forte), nunca em texto
  puro, em log ou em DTO. Falhas de login (e-mail inexistente, senha incorreta, conta sem
  senha) DEVEM responder a MESMA mensagem genérica e status, em tempo equivalente.

**Rationale**: regras explícitas de armazenamento e proteção impedem vazamento de credenciais
e endpoints desprotegidos, independentemente do esquema de autenticação adotado. O acesso sem
senha, mesmo restrito a ambientes internos, é uma porta aberta em bancos com dados reais de
homologação; o cadastro + login por senha cobre o mesmo uso (testes, Bruno, desenvolvimento
local) sem o risco.

### VII. Ambientes e Testes de API

Os três ambientes DEVEM ser criados e mantidos com a skill `dotnet-env`:

| Ambiente | `ASPNETCORE_ENVIRONMENT` | Como roda |
|---|---|---|
| Dev | `Development` | Local, sem Docker (`dotnet run`, PostgreSQL instalado na máquina) |
| Homolog | `Homolog` | Docker local: `docker compose` no WSL com frontend, API, banco e gateway |
| Prod | `Production` | Pipeline do GitHub Actions que publica tudo no servidor de produção |

- O ambiente de homologação DEVE subir no Docker local (`docker compose up -d --build` via skill
  `wsl-docker`), com as migrações aplicadas pelo próprio compose.
- Ao final de cada ciclo de desenvolvimento (feature, história ou correção que altere a API), a
  homologação DEVE ser reconstruída com o código do ciclo e os testes de API
  (`{NomeDoProjeto}.ApiTests`) DEVEM ser executados contra ela
  (`ApiBaseUrl=http://localhost:8080/api`), **criando seus próprios usuários pelo cadastro e
  login com senha** (sem usuário pré-existente nem atalho sem senha). O ciclo só termina com
  todos os testes passando.
- Endpoints novos ou alterados DEVEM ganhar ou atualizar testes de API, criados com a skill
  `dotnet-test-api`, no mesmo ciclo.
- Falhas nos testes de API DEVEM ser corrigidas no código, não contornadas removendo ou
  desabilitando testes. Uma exceção DEVE ser justificada explicitamente no PR.

**Rationale**: a homologação reproduz a topologia de produção (gateway, prefixo `/api`, proxy
reverso, banco em container). Testar a API ali ao fim de cada ciclo pega problemas de
configuração e integração que os testes de unidade não veem.

### VIII. Estratégia de Testes

| Camada | Tipo de teste permitido | Ferramentas |
|---|---|---|
| Backend | Somente testes de API (Princípio VII) | xUnit + Flurl.Http + FluentAssertions (skill `dotnet-test-api`) |
| Frontend | Somente testes unitários | Runner unitário compatível com Vite (ex.: Vitest) + Testing Library |

- O frontend DEVE ser testado apenas com testes unitários (componentes, hooks, services e
  utilitários isolados, com dependências externas simuladas).
- Playwright, Cypress, Selenium e qualquer outro teste E2E ou de automação de navegador no
  frontend são PROIBIDOS. NÃO adicionar suas dependências, configurações, pastas ou etapas de
  pipeline.
- O backend NÃO DEVE ter testes de unidade ou de integração paralelos aos testes de API; a
  cobertura do backend vem dos testes de API executados contra a homologação.
- Fluxos de ponta a ponta são cobertos indiretamente: a API pelos testes de API e a interface
  pelos testes unitários do frontend.

**Rationale**: testes E2E de navegador são lentos, frágeis e caros de manter. Testes de API
validam o contrato e a integração real do backend, e testes unitários dão feedback rápido no
frontend sem dependência de navegador ou ambiente.

## Restrições Adicionais

### Variáveis de Ambiente

**Backend**

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ConnectionStrings__{NomeDoProjeto}Context` | Sim | Connection string PostgreSQL |
| `ASPNETCORE_ENVIRONMENT` | Sim | Development (local, sem Docker), Homolog (docker compose no WSL), Production |

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
- [ ] Leituras de repositório filtram por `userId`.
- [ ] Nenhuma rota/flag de emissão de token sem credencial foi adicionada; testes de API e
      coleções obtêm o token por cadastro + login.
- [ ] Comandos Docker locais seguiram a skill `wsl-docker`.
- [ ] Ambientes Dev, Homolog e Prod seguem a skill `dotnet-env`.
- [ ] Ao final do ciclo, a homologação foi reconstruída e os testes de API passaram contra ela.
- [ ] O frontend foi testado apenas com testes unitários (sem Playwright nem E2E).

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
  `react-architecture`, `dotnet-graphql`, `wsl-docker`, `dotnet-env` e `dotnet-test-api`.

**Version**: 3.3.0 | **Ratified**: 2026-04-02 | **Last Amended**: 2026-10-26
