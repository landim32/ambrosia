# Proposta de emenda — Constituição do Ambrosio 3.2.0 → 3.3.0

**Feature**: 003-email-password-jwt-auth · **Tipo**: MINOR (expansão material do esquema de
autenticação + regra mais restritiva; nenhuma remoção de princípio) · **Entra em vigor**: na
aprovação do PR desta feature, quando o texto abaixo é aplicado em
`.specify/memory/constitution.md` (com o Sync Impact Report atualizado).

## Justificativa

A spec 003 remove `POST /auth/dev-login` de **todos** os ambientes e adiciona cadastro e login
com e-mail e senha. O Princípio VI atual (a) descreve o esquema como "JWT emitido após login
OAuth/OIDC (Google)" e (b) permite explicitamente o dev-login em Development e Homolog. Sem a
emenda, a constituição descreveria uma rota que não existe e um esquema incompleto, e um
contribuidor futuro poderia reintroduzir a entrada sem senha amparado pelo texto.

## Alterações no Princípio VI — Autenticação e Segurança

### Tabela "Esquema" (substituir a linha)

| Aspecto | Padrão |
|---|---|
| Esquema | JWT Bearer HS256 emitido pela própria API (`sub` = `user_id`, `email`, `name`; `iss`/`aud` configuráveis; expiração padrão de 8 h), obtido por **uma** das rotas públicas: `POST /auth/register` (cadastro com nome, e-mail e senha — já devolve a sessão), `POST /auth/login` (e-mail e senha) ou `POST /auth/google` (troca do ID token do Google). As três devolvem o mesmo `LoginResult`. |

### Regras de segurança (substituir a última regra e acrescentar duas)

Remover:

> - `POST /auth/dev-login` (token sem provedor) é permitido SOMENTE em Development e Homolog: em
>   Production responde 404 e é omitido do Swagger.

Acrescentar:

> - É PROIBIDA qualquer rota, flag de build ou atalho que emita token sem credencial
>   (ex.: o antigo `POST /auth/dev-login`), em QUALQUER ambiente, inclusive Development e
>   Homolog. Testes de API, coleções HTTP e quickstarts DEVEM obter o token por
>   `POST /auth/register` + `POST /auth/login` com usuários criados pela própria execução.
> - Senhas DEVEM ser armazenadas apenas como hash com salt aleatório por senha, com algoritmo
>   de derivação lenta (PBKDF2-HMAC-SHA256 ≥ 600.000 iterações ou mais forte), nunca em texto
>   puro, em log ou em DTO. Falhas de login (e-mail inexistente, senha incorreta, conta sem
>   senha) DEVEM responder a MESMA mensagem genérica e status, em tempo equivalente.

### Rationale (acrescentar ao final)

> O acesso sem senha, mesmo restrito a ambientes internos, é uma porta aberta em bancos com
> dados reais de homologação; o cadastro + login por senha cobre o mesmo uso (testes, Bruno,
> desenvolvimento local) sem o risco.

## Alterações no Princípio VII — Ambientes e Testes de API (esclarecimento)

Acrescentar ao segundo item da lista:

> …e os testes de API (`{NomeDoProjeto}.ApiTests`) DEVEM ser executados contra ela
> (`ApiBaseUrl=http://localhost:8080/api`), **criando seus próprios usuários pelo cadastro e
> login com senha** (sem usuário pré-existente nem atalho sem senha). O ciclo só termina com
> todos os testes passando.

## Quality gates (acrescentar um item)

> - [ ] Nenhuma rota/flag de emissão de token sem credencial foi adicionada; testes de API e
>   coleções obtêm o token por cadastro + login.

## Sync Impact Report (cabeçalho do arquivo)

```text
Version change: 3.2.0 → 3.3.0 (MINOR)
Bump rationale: Princípio VI passa a descrever cadastro/login com e-mail e senha ao lado do
Google e PROÍBE emissão de token sem credencial em qualquer ambiente (o dev-login, antes
permitido em Development/Homolog, foi removido pela feature 003). Princípio VII esclarece
que os testes de API criam seus próprios usuários. Novo quality gate correspondente.
Modified principles: VI (esquema + regras), VII (esclarecimento).
Added sections: none. Removed sections: none.
Templates/skills afetados: nenhum (dotnet-test-api já usa fixture com login real).
Follow-up: histórias 007/009 (docs/historias) mencionavam o dev-login como possível
bootstrap do bot/MCP — devem usar cadastro + login ou token próprio.
```
