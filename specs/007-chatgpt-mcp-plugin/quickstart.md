# Quickstart: Plugin do Ambrosio no ChatGPT

**Feature**: `007-chatgpt-mcp-plugin`. Guia para subir, verificar e conectar o plugin. Comandos Docker seguem a skill `wsl-docker` (Docker Engine dentro do WSL2).

## 1. Configuração

Acrescentar ao `.env` (modelo em `.env.example`):

```dotenv
# Authorization server e MCP (feature 007)
AUTH_ISSUER=http://localhost:8080/auth           # em produção: https://ambrosiaai.site/auth
MCP_RESOURCE_URL=http://localhost:8080/mcp       # em produção: https://ambrosiaai.site/mcp
AUTH_SIGNING_KEY_PEM="-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----"
```

Gerar a chave uma vez e guardar como segredo:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out mcp-signing.pem
```

`AUTH_GOOGLE_CLIENT_ID`, banco, CPF e storage já existem e são reaproveitados pelo serviço `mcp`.

## 2. Subir a homologação

```bash
wsl -e bash -lc "cd /mnt/c/repos/ambrosia && docker compose --profile frontend up -d --build"
```

O `migrator` aplica a migração `AddOAuthServer`. O serviço novo `ambrosio-homolog-mcp` fica atrás do gateway em `/auth/`, `/mcp` e `/.well-known/oauth-*`.

## 3. Verificações rápidas (curl)

```bash
# Descoberta (JSON, não HTML)
curl -s http://localhost:8080/.well-known/oauth-protected-resource/mcp
curl -s http://localhost:8080/.well-known/oauth-authorization-server/auth
curl -s http://localhost:8080/auth/.well-known/openid-configuration

# Listagem de tools sem token (permitida): 28 tools
curl -s -X POST http://localhost:8080/mcp -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

# Token inválido → 401 com WWW-Authenticate resource_metadata
curl -si -X POST http://localhost:8080/mcp -H 'Authorization: Bearer invalido' \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | head -5
```

## 4. Testes de API (fim do ciclo, Princípio VII)

```bash
cd backend
dotnet test Ambrosio.ApiTests --filter "FullyQualifiedName~McpServer" -e ApiBaseUrl=http://localhost:8080/api
dotnet test Ambrosio.ApiTests -e ApiBaseUrl=http://localhost:8080/api   # suíte inteira
```

A suíte cria os próprios usuários (cadastro + login), registra um cliente por DCR com a URI de teste de Homolog, faz o fluxo de autorização com PKCE e consentimento por HTTP e chama o MCP com o token obtido. Ela cobre:

- descoberta (PRM e metadata do AS, mesmo conteúdo nos dois endereços);
- PKCE obrigatório e recusa de `plain`;
- `resource` obrigatório e exato;
- `iss` na resposta de autorização;
- rotação de refresh token e revogação da família no reuso;
- 401 para token inválido, inclusive o HS256 da API;
- desafio `insufficient_scope` com `_meta["mcp/www_authenticate"]`;
- isolamento entre duas vendedoras;
- prévia sem gravação e gravação com confirmação auditada;
- CPF sempre mascarado;
- limite de 200 linhas;
- paridade Swagger × `tools/list`;
- `iniciar_importacao` devolvendo o link, sem aceitar senha.

## 5. Conectar no ChatGPT (modo desenvolvedor)

O ChatGPT só conecta em HTTPS público:

1. Abrir um túnel HTTPS para `http://localhost:8080` (qualquer serviço de túnel) e anotar a URL pública, por exemplo `https://ambrosio-dev.exemplo.net`.
2. Ajustar no `.env`: `AUTH_ISSUER=https://ambrosio-dev.exemplo.net/auth` e `MCP_RESOURCE_URL=https://ambrosio-dev.exemplo.net/mcp`. Recriar o serviço `mcp`.
3. No Google Cloud, incluir a URL do túnel nas origens JavaScript autorizadas do Client ID, para o botão do Google funcionar na tela de login do AS.
4. Criar o plugin conforme [contracts/plugin-package.md](contracts/plugin-package.md#uso-no-modo-desenvolvedor), com a URL `https://ambrosio-dev.exemplo.net/mcp` e autenticação OAuth.
5. Rodar o roteiro de verificação da [spec](spec.md#roteiro-de-verificação-no-chatgpt) e anotar a tool escolhida em cada prompt (meta: 90% certas, SC-006).
6. Conferir no log do gateway quais endereços de `/.well-known/` o ChatGPT consultou (risco da descoberta com caminho).

Em produção, os passos 1 a 3 não são necessários: `AUTH_ISSUER` e `MCP_RESOURCE_URL` apontam para `https://ambrosiaai.site`.

## 6. Checklist de aceite manual

- [ ] Conectar do zero até a primeira resposta em menos de 2 minutos (SC-001).
- [ ] Negar o consentimento: nada é acessado.
- [ ] Autorizar só leitura de clientes, pedir um cadastro e ampliar a autorização pela oferta do ChatGPT.
- [ ] Cadastrar uma cliente com prévia e confirmação e ver a cliente nas telas.
- [ ] Pedir importação, abrir o link, digitar a senha na tela do Ambrosio e acompanhar pela conversa.
- [ ] Desconectar e reconectar sem erro, mesma conta reconhecida.
- [ ] Nenhuma resposta com CPF completo ou senha do +Vendas.

## 7. Reforços futuros (fora desta feature)

- Validar o mTLS do ChatGPT no nginx (SAN `mtls.prod.connectors.openai.com`).
- Publicar no diretório: páginas de privacidade, termos e suporte, desafio `/.well-known/openai-apps-challenge`, conta de teste e material de revisão.
