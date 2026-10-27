# Contrato — endpoints de autenticação (delta sobre `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml`)

Este arquivo descreve **o que muda** no contrato consolidado da API. Na implementação, o
`openapi.yaml` da 001 é atualizado com estes fragmentos (ele é a referência do
`ContractAlignmentOperationFilter` e do Swagger). Formato de erro, `LoginResult`, `UserInfo`,
`GoogleLoginInfo` e `securitySchemes` permanecem como estão.

## Rotas adicionadas

```yaml
paths:
  /auth/register:
    post:
      tags: [Auth]
      security: []
      summary: Cria uma conta com nome, e-mail e senha e já inicia a sessão
      description: |
        O e-mail é normalizado (minúsculas, sem espaços nas pontas) e deve ser único.
        A senha é guardada apenas como hash (PBKDF2-HMAC-SHA256) e nunca é devolvida.
        Responde 409 se o e-mail já existe — com orientação para entrar com Google quando a
        conta existente foi criada pelo Google.
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/RegisterInfo' }
      responses:
        '201':
          description: Conta criada; sessão iniciada
          content:
            application/json:
              schema: { $ref: '#/components/schemas/LoginResult' }
        '400': { $ref: '#/components/responses/ValidationError' }
        '409':
          description: E-mail já cadastrado
          content:
            application/json:
              schema: { $ref: '#/components/schemas/ErrorResult' }
              examples:
                senha:
                  value: { sucesso: false, mensagem: "Este e-mail já está cadastrado.", erros: [] }
                google:
                  value: { sucesso: false, mensagem: "Este e-mail já está cadastrado. Entre com sua conta Google.", erros: [] }
        '500': { $ref: '#/components/responses/ServerError' }

  /auth/login:
    post:
      tags: [Auth]
      security: []
      summary: Entra com e-mail e senha
      description: |
        Devolve o mesmo token do login Google (HS256, 8 h, claims sub/email/name).
        E-mail inexistente, senha incorreta ou conta sem senha (só Google) respondem o MESMO
        401 com a mesma mensagem, em tempo equivalente.
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/LoginInfo' }
      responses:
        '200':
          description: Token do Ambrosio
          content:
            application/json:
              schema: { $ref: '#/components/schemas/LoginResult' }
        '400': { $ref: '#/components/responses/ValidationError' }
        '401':
          description: Credenciais inválidas (mensagem única, sem distinguir o motivo)
          content:
            application/json:
              schema: { $ref: '#/components/schemas/ErrorResult' }
              example: { sucesso: false, mensagem: "E-mail ou senha inválidos", erros: [] }
        '500': { $ref: '#/components/responses/ServerError' }
```

## Rota alterada

```yaml
  /auth/google:
    post:
      # inalterada na forma; descrição ganha a regra FR-009:
      summary: Login com ID token do Google; cria o usuário no primeiro acesso
      description: |
        Se o e-mail já tem conta, a conta é reutilizada (nome e subject atualizados).
        Se essa conta tinha senha, a senha é REMOVIDA neste login: a partir daí só o Google
        entra, até uma futura recuperação/troca de senha. Sessões já emitidas por senha
        continuam válidas até expirar.
```

## Rota removida

```yaml
  /auth/dev-login:   # REMOVIDA em todos os ambientes. Responde 404 como qualquer rota
                     # inexistente e não aparece no documento OpenAPI.
```

`components.schemas.DevLoginInfo` também é removido.

## Schemas adicionados

```yaml
components:
  schemas:
    RegisterInfo:
      type: object
      required: [name, email, password]
      properties:
        name:     { type: string, minLength: 1, maxLength: 120, example: "Ana" }
        email:    { type: string, format: email, maxLength: 254, example: "ana@exemplo.com" }
        password: { type: string, format: password, minLength: 8, maxLength: 128, writeOnly: true }

    LoginInfo:
      type: object
      required: [email, password]
      properties:
        email:    { type: string, format: email, example: "ana@exemplo.com" }
        password: { type: string, format: password, writeOnly: true }

    UserInfo:
      # alteração: enum do provedor
      properties:
        provider: { type: string, enum: [google, password] }   # antes: [google, dev]
```

## Mensagens de validação (400, `erros[].campo` / `erros[].mensagem`)

| campo | mensagens possíveis |
|---|---|
| `name` | "O nome é obrigatório." · "O nome deve ter no máximo 120 caracteres." |
| `email` | "O e-mail é obrigatório." · "E-mail inválido." · "O e-mail deve ter no máximo 254 caracteres." |
| `password` | "A senha é obrigatória." · "A senha deve ter entre 8 e 128 caracteres." |
| `body` | "Valor inválido." (JSON malformado) |

## Descrição do esquema Bearer (Swagger)

"Cole o token do Ambrosio obtido em `POST /auth/login`, `POST /auth/register` ou
`POST /auth/google`. Não inclua o prefixo `Bearer`."

## Consumo pelo frontend (`apiClient`)

| Resposta | Classe lançada | Uso na tela |
|---|---|---|
| 200/201 | — (`LoginResult`) | `writeSession` + navegação para `returnTo` |
| 400 | `ApiError(400, "Dados inválidos.", erros)` | erro por campo (`erros[].campo`) |
| 409 | `ApiError(409, mensagem)` | erro geral do formulário de cadastro |
| 401 **em chamada anônima** | `ApiError(401, mensagem)` — **não** dispara o handler de sessão expirada (research §3) | erro geral do formulário de entrada |
| 401 em chamada autenticada | `UnauthorizedError` + handler (inalterado) | `logout('expirada')` |
| rede | `NetworkError` | "Não foi possível conectar" + manter a senha digitada |
