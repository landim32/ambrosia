# Research: Plugin do Ambrosio no ChatGPT (007-chatgpt-mcp-plugin)

**Data**: 2026-10-27 | **Spec**: [spec.md](spec.md) | **HU**: [005](../../docs/historias/005-plugin-chatgpt-mcp.md)

Cada item: **Decisão**, **Motivo**, **Alternativas consideradas**. Fontes consultadas em 27/10/2026.

---

## R1. SDK do servidor MCP

- **Decisão**: SDK oficial C# `ModelContextProtocol.AspNetCore` (série 2.x, estável desde 28/07/2026), com versão **fixada** no `.csproj` na hora da implementação (a mais recente estável que compila em `net8.0`). Tools com `[McpServerToolType]`/`[McpServerTool]` e anotações pelos parâmetros do atributo.
- **Motivo**: é o SDK mantido pelo projeto MCP. Já traz o transporte HTTP streamable, o modo *stateless* e um esquema de autenticação próprio (`AddMcp`) que publica o Protected Resource Metadata e monta o `WWW-Authenticate` com `resource_metadata` (amostra `samples/ProtectedMcpServer` do repositório). Reaproveita o domínio .NET sem *token passthrough*.
- **Alternativas**: SDK TypeScript (`@modelcontextprotocol/sdk`), descartado porque obrigaria chamar a API por HTTP e repassar token, o que a especificação MCP proíbe. Implementação manual do protocolo JSON-RPC, descartada pelo custo e pelo risco de divergir da especificação.
- **A confirmar na implementação**: se o descritor de tool aceita `securitySchemes` e `_meta` arbitrários. Se não aceitar, um filtro de `tools/list` acrescenta os campos (R6).

## R2. Modo do transporte: stateless

- **Decisão**: `HttpServerSessionMode.Stateless` (padrão recomendado pelo SDK).
- **Motivo**: escala sem afinidade de sessão e funciona com clientes que não mandam `Mcp-Session-Id`. A única funcionalidade que exigiria sessão seria a *elicitation*, e ela foi trocada por um link (R9).
- **Alternativas**: com sessão, descartado porque traz estado no servidor só para a *elicitation*, que tem substituto simples.

## R3. Authorization server

- **Decisão**: authorization server próprio com **OpenIddict 7.7.x** (última estável em 10/10/2026: 7.7.2, com builds `net8.0`), com armazenamento EF Core no mesmo PostgreSQL e no mesmo `AmbrosioContext`.
- **Motivo**: o `sub` do token é o `user_id` do Ambrosio sem sincronização; login Google e e-mail/senha reaproveitam as regras de `users` das features 001 e 003; CIMD e `resource` ficam sob nosso controle (handlers do OpenIddict); mesma stack (.NET 8, EF Core, PostgreSQL); sem custo de licença. A 7.7.0 corrigiu uma falha de validação de `aud` em client assertions (GHSA-925x-4h4v-2792): usar 7.7.0 ou superior.
- **Alternativas**: Keycloak (DCR e RFC 9207 prontos, mas outra stack e sincronização de usuários por e-mail); Auth0 (gerenciado, mas `resource`/CIMD incertos e custo ao crescer); Duende IdentityServer (suporta RFC 8707 e RFC 9207, mas tem licença comercial). Fica como plano B o Keycloak, se CIMD no OpenIddict se mostrar inviável.
- **Itens sem suporte nativo confirmado no OpenIddict** (as notas de versão não citam): CIMD, DCR, parâmetro `iss` na resposta (RFC 9207) e `resource` (RFC 8707). Cada um vira um handler do pipeline do servidor (R4, R5).

## R4. Registro do cliente ChatGPT: CIMD e DCR

- **Decisão**: suportar os dois.
  - **CIMD (preferido pelo ChatGPT)**: quando o `client_id` é uma URL `https`, um handler de validação da requisição de autorização e de token baixa o documento (só `https`, host na lista `Auth:Cimd:AllowedHosts` com `chatgpt.com`, sem IP privado, *timeout* de 5 s, limite de 64 KB, cache pelos headers HTTP), confere `client_id` igual à URL e `redirect_uri` contida em `redirect_uris`, e aceita `token_endpoint_auth_method` `none` (ou `private_key_jwt` com o JWKS do documento). A aplicação correspondente é criada ou atualizada no `IOpenIddictApplicationManager` para o OpenIddict seguir o fluxo normal.
  - **DCR**: `POST /auth/connect/register` próprio, que aceita só `redirect_uris` da lista `Auth:AllowedRedirectUris` e cria cliente público (`none`) sem expiração.
- **Motivo**: a documentação de autenticação de plugins diz que o ChatGPT prioriza CIMD, mas o construtor do plugin pode escolher DCR. Manter os dois evita reprovação se o modo mudar.
- **Alternativas**: só DCR (mais simples, mas fora da preferência atual); cliente estático com segredo (o pacote do plugin não aceita segredos e o portal pede o segredo na configuração da conexão, o que complica o uso próprio).

## R5. `resource` (RFC 8707) e `iss` na resposta (RFC 9207)

- **Decisão**:
  - Handler na autorização e no token que exige `resource` igual a `Mcp:ResourceUrl` (`https://ambrosiaai.site/mcp`), por comparação exata (com barra no final é recusado), e define o `aud` do access token com esse valor (`SetResources`/`SetAudiences`).
  - Handler na resposta de autorização (sucesso e erro) que acrescenta `iss` = issuer, e metadata com `authorization_response_iss_parameter_supported: true`.
- **Motivo**: são requisitos R5 e R6 do ChatGPT. Com `iss` na resposta, o ChatGPT usa a redirect URI estável `https://chatgpt.com/connector_platform_oauth_redirect`.
- **Alternativas**: não anunciar RFC 9207 e usar a redirect por `callback_id`, descartado porque a URI muda por conexão e complica a lista de permitidas.

## R6. `securitySchemes`, anotações e desafio por tool

- **Decisão**: cada tool declara anotações (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`, `Title`) e um atributo próprio `[AmbrosioScopes("customers:read")]`. Um filtro de chamada de tool confere os escopos do token antes de executar; faltando token ou escopo, devolve `isError: true` com `_meta["mcp/www_authenticate"]` (`error`, `error_description`, `scope`). O mesmo atributo alimenta `securitySchemes` no `tools/list` (direto, se o SDK aceitar, ou por filtro de listagem). Cada tool também traz `_meta["ambrosio/endpoint"]` com o endpoint da API equivalente (R12).
- **Motivo**: requisito R8 do ChatGPT. Um ponto único de checagem evita esquecer escopo numa tool.
- **Alternativas**: política de autorização por endpoint MCP, descartada porque o MCP tem um único endpoint HTTP para todas as tools.

## R7. Autenticação "mista" no endpoint MCP

- **Decisão**: o endpoint `/mcp` **não** exige token para `initialize`, `tools/list` e a tool `noauth` (`sobre_ambrosio`). Token presente e inválido (assinatura, `iss`, `aud`, expiração) responde **HTTP 401** com `WWW-Authenticate: Bearer resource_metadata="…"`. Tool que exige conta, chamada sem token, devolve o resultado com `_meta["mcp/www_authenticate"]`, que dispara a UI de vinculação. No pacote, `mcp.json` declara `auth.type: "mixed"`.
- **Motivo**: a documentação de plugins diz que listar `noauth` e `oauth2` indica acesso anônimo com desbloqueio autenticado. O ChatGPT inicia o OAuth na primeira chamada de tool protegida.
- **Ajuste em relação à HU 005**: o critério 4 da HU ("sem token responde 401") vale para token **inválido**. Sem token, a listagem é aberta e as tools protegidas devolvem o desafio no resultado. Se a decisão for não ter `sobre_ambrosio`, o endpoint pode exigir token sempre (401 sem token), sem outro impacto.
- **Alternativas**: exigir token em tudo (`RequireAuthorization`), descartado porque impede a tool `noauth`.

## R8. Hospedagem: um processo novo para MCP e authorization server

- **Decisão**: **um** projeto novo, `Ambrosio.McpServer` (ASP.NET Core), que hospeda o authorization server em `/auth`, o MCP em `/mcp` e os documentos `/.well-known/oauth-*`. Um serviço novo `mcp` no `docker-compose.yml` e no `docker-compose-prod.yml`, com o gateway roteando `/auth/`, `/mcp` e `/.well-known/oauth-` para ele (hoje essas rotas respondem 503 em `deploy/nginx/routes.inc`).
- **Motivo**: menos peças para operar (uma imagem e um container), mesma origem para o cookie do login e para o MCP, e o mesmo `ConfigureServices` da camada `Application`. A HU previa três projetos (`Ambrosio.Mcp`, `Ambrosio.McpServer`, `Ambrosio.Auth`); juntar não muda nenhum contrato, e separar depois é mecânico.
- **Alternativas**: dois processos (MCP e AS), descartado por dobrar a operação sem ganho no uso próprio; dentro da `Ambrosio.API`, descartado porque mistura o JWT HS256 das telas com o JWT do AS no mesmo pipeline e porque o MCP teria a escala da API.

## R9. Credenciais do +Vendas em `iniciar_importacao`

- **Decisão**: a tool **não** inicia a execução. Ela valida o período, pede confirmação e devolve um link `https://ambrosiaai.site/importacao?inicio=AAAA-MM-DD&fim=AAAA-MM-DD`. A tela de importação passa a ler esses dois parâmetros da URL para preencher o período. A vendedora digita login e senha lá e inicia. As demais tools de importação acompanham a execução normalmente.
- **Motivo**: FR-024/FR-025. Argumentos de tool ficam no histórico da conversa e nos logs do provedor da LLM. Com o modo *stateless*, a *elicitation* em modo URL não está disponível, e o link em texto entrega o mesmo resultado.
- **Alternativas**: *elicitation* em modo URL (exige sessão, R2); senha como argumento (viola FR-025).

## R10. Telas de login e consentimento do authorization server

- **Decisão**: Razor Pages no `Ambrosio.McpServer`, em `/auth/login` e `/auth/consent`, com CSS próprio enxuto usando os tokens visuais do Ambrosio (tema escuro, azul `#005CF2`, mascote). Login por e-mail e senha (reaproveitando o serviço de login da feature 003) e pelo botão do Google Identity Services, cujo ID token é conferido pelo mesmo serviço do `POST /auth/google`. A sessão do login no AS fica num cookie `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/auth`, de curta duração.
- **Motivo**: o fluxo OAuth exige que o AS mostre login e consentimento na própria origem e lembre quem entrou entre o `authorize` e o consentimento. Sem SPA envolvida, o cookie de sessão do servidor é o mecanismo padrão. O Google pelo GIS dispensa um redirect URI novo no Google Cloud (só a origem JavaScript `https://ambrosiaai.site`, que já existe).
- **Alternativas**: telas no frontend React com o token do AS em `localStorage` (complicaria o fluxo e exporia o código de autorização ao SPA); redirect clássico do Google (exige redirect URI novo e não reaproveita o `POST /auth/google`).

## R11. Prévia antes de gravar

- **Decisão**: toda tool de escrita recebe `confirmar` (padrão `false`). Com `false`, executa só a validação (validators FluentValidation e checagens de existência e duplicidade, sem gravar) e devolve a prévia. Com `true`, chama o mesmo serviço de domínio que o controller.
- **Motivo**: FR-018. O ChatGPT também pede aprovação nas tools com `readOnlyHint: false` (camada extra).
- **Alternativas**: confiar só na aprovação do cliente, descartado porque a spec exige prévia.

## R12. Paridade com a API como teste de API

- **Decisão**: a paridade (FR-011) é verificada por um **teste de API**: lê as operações do Swagger da API (`/api/swagger/v1/swagger.json`) e o `tools/list` do MCP (cada tool declara `_meta["ambrosio/endpoint"]`, por exemplo `"POST /customers"`), e falha quando uma operação, fora de `/auth/*`, não tem tool.
- **Motivo**: a constituição (Princípio VIII) permite no backend só testes de API. Um teste por reflexão dentro do projeto seria teste de unidade.
- **Alternativas**: teste por reflexão nos controllers, descartado pela constituição.

## R13. Chave de assinatura dos tokens

- **Decisão**: RSA 2048 em PEM (PKCS#8) via variável `Auth__SigningKeyPem`, obrigatória em Homolog e Production. Em Development, sem a variável, gera e grava uma chave local. A chave pública sai no JWKS. Access token JWT **não** criptografado (`DisableAccessTokenEncryption`), para o MCP validar pelo JWKS.
- **Motivo**: a chave precisa sobreviver a reinícios, senão toda conexão do ChatGPT cai. Variável de ambiente segue o Princípio VI (segredos por ambiente).
- **Alternativas**: certificado de desenvolvimento do OpenIddict (efêmero); cofre externo (custo, sem necessidade no uso próprio).

## R14. Validades e refresh

- **Decisão**: access token de 30 min; refresh token de 14 dias, deslizante, com rotação e revogação da família em caso de reuso; `offline_access` anunciado e concedido. Autorização permanente guardada no OpenIddict, para não pedir consentimento a cada religação.
- **Motivo**: premissa da spec. Se o ChatGPT não pedir `offline_access`, o AS emite refresh token mesmo assim para clientes do ChatGPT (a conferir no teste em modo desenvolvedor).

## R15. Tabelas do OpenIddict nas convenções do banco

- **Decisão**: entidades do OpenIddict com chave `long`, tabelas `oauth_applications`, `oauth_authorizations`, `oauth_scopes` e `oauth_tokens`, colunas em snake_case e chaves `{entidade}_id` mapeadas por Fluent API no `AmbrosioContext`. Relações sem cascata (`ClientSetNull`), sobrescrevendo o padrão do OpenIddict. Migração `AddOAuthServer`, aplicada pelo `migrator` como as demais.
- **Motivo**: Princípio V (snake_case, `bigint`, sem cascata). A remoção de tokens e autorizações vencidos fica com um `BackgroundService` simples que chama `PruneAsync` dos gerenciadores de tokens e autorizações do OpenIddict, sem a integração com Quartz.
- **A confirmar na implementação**: o comportamento de exclusão padrão que o OpenIddict configura nas relações.

## R16. Auditoria das gravações

- **Decisão**: tabela `mcp_audit_entries` gravada pelo mesmo filtro de tool em toda chamada de escrita confirmada (e nas recusadas por regra de negócio), com argumentos já sem CPF completo, senha ou conteúdo de arquivo.
- **Motivo**: FR-020. A tabela é consultável e sobrevive a reinícios, ao contrário do log de console.

## R17. Origem, rate limit e testes em homologação

- **Decisão**: middleware que recusa requisições ao `/mcp` com header `Origin` fora de `Mcp:AllowedOrigins` (chamadas do ChatGPT são servidor a servidor e não mandam `Origin` de navegador); `RateLimiter` do ASP.NET Core por `sub` (por exemplo, 60 chamadas por minuto); `proxy_buffering off` e `proxy_read_timeout` alto na `location /mcp`. Para o teste no ChatGPT em homologação, um túnel HTTPS para o gateway (`http://localhost:8080`), com `Auth:Issuer` e `Mcp:ResourceUrl` configurados com a URL do túnel.
- **Motivo**: proteção contra DNS rebinding exigida pelo transporte HTTP do MCP; ChatGPT só conecta em HTTPS público.

## R18. mTLS do ChatGPT

- **Decisão**: não validar mTLS nesta feature (uso próprio). Fica documentado no quickstart como reforço futuro no nginx (SAN `mtls.prod.connectors.openai.com`).
- **Motivo**: o mTLS autentica o ChatGPT, não a vendedora. O OAuth já protege os dados, e a configuração depende do TLS de produção.

Fontes:
- [OpenAI — Autenticação de plugins](https://developers.openai.com/plugins/build/auth)
- [OpenAI — Empacotamento de plugins](https://developers.openai.com/plugins/build/plugins)
- [OpenAI — Conectar e testar](https://developers.openai.com/plugins/deploy/connect-chatgpt)
- [MCP C# SDK — repositório](https://github.com/modelcontextprotocol/csharp-sdk) e [amostra ProtectedMcpServer](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/samples/ProtectedMcpServer/Program.cs)
- [NuGet — ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol/absoluteLatest)
- [OpenIddict — releases](https://github.com/openiddict/openiddict-core/releases)

## R19. Bloqueio de versão: o `PruneAsync` do OpenIddict não roda sobre o EF Core 9 do projeto

Registrado na implementação do T017, 2026-10-28. **T017 ficou suspenso**, não concluído.

- **Sintoma (em runtime, nunca em build):** `System.MissingMethodException: Method not found: 'Task<Int32> Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteDeleteAsync(IQueryable<T>, CancellationToken)'`, lançado por `OpenIddictEntityFrameworkCoreTokenStore.PruneAsync` no primeiro ciclo do `OAuthPruneService`. Ocorreu em 100% dos ciclos, em todas as rodagens.
- **Causa:** `OpenIddict.EntityFrameworkCore` 7.7.2 resolve para o asset `lib/net8.0` num host em net8.0, e esse asset foi compilado contra `Microsoft.EntityFrameworkCore.Relational` **8.0.30**, onde `ExecuteDeleteAsync` está declarado. O projeto piniza EF Core **9.0.22** porque `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4 puxa a linha 9.x (nota de versionamento no `Ambrosio.Infra.csproj`); no EF Core 9 o método saiu desse assembly. Conferido por leitura dos binários: `Relational` 8.0.30 contém `ExecuteDeleteAsync`, `Relational` 9.0.22 não contém (embora ainda exponha `RelationalQueryableExtensions`), e o assembly `Microsoft.EntityFrameworkCore` 9.0.22 contém.
- **Por que o build não pega:** a ligação é resolvida em JIT, no primeiro uso. Compilação, análise e a subida do host ficam verdes — `Application started` não exercita o caminho.
- **Decisão:** adiar. O `OAuthPruneService` deixa de ser registrado no arranque e o host emite um `LogWarning` explícito de que a limpeza está desabilitada, em vez de rodar um serviço que falha para sempre atrás de um aviso genérico. A classe permanece no repositório; resolver o T017 é reimplementar a limpeza com LINQ próprio sobre `oauth_tokens`/`oauth_authorizations` (o `ExecuteDeleteAsync` real do EF 9, que já compila no `Ambrosio.Infra` — ver `ImportRunRepository.MarkInterruptedAsync`, que usa `ExecuteUpdateAsync`) e religar o `AddHostedService`.
- **Descartado:** baixar EF Core para 8.x, e subir o TFM do host para net9/net10 só para casar com o asset `net9.0` do OpenIddict — os dois violam o Princípio I da constituição (.NET 8.0 / EF Core 9.x) e o segundo ainda separaria o host da API e da Infra em TFMs diferentes.
- **Consequência aceita:** `oauth_tokens` e `oauth_authorizations` crescem sem limite. Não afeta a autenticação (o MCP valida o token por assinatura e `aud`, consultando o banco por token em nenhuma rota), mas infla o banco e o backup de homologação.

Fontes:
- nuspec do `OpenIddict.EntityFrameworkCore` 7.7.2 (grupos `net8.0` → Relational 8.0.30, `net9.0` → 9.0.19, `net10.0` → 10.0.11), lido em `api.nuget.org`
- `project.assets.json` do `Ambrosio.McpServer` (asset `lib/net8.0` selecionado; EF Core 9.0.22 resolvido)
