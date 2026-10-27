# Decisões do autopiloto — 003-email-password-jwt-auth

Execução 20261026-172724 · história 1/1 "Alterar o login:"

## specify · 2026-10-26 20:30
**Pergunta:** Q1 — Login com Google: deve ser mantido ao lado do login com e-mail e senha, ou removido?
**Opções:** A: Manter Google + senha (Recommended) (recomendada) · B: Remover Google
**Escolha:** A: Manter Google + senha (Recommended)
**Por quê:** A opção A está marcada como recomendada. Além disso, a história só pede remover o /auth/dev-login e adicionar e-mail/senha, sem mencionar a remoção do Google. Manter o Google evita ampliar escopo e risco.
_claude-code / claude-sonnet-5-5_

## specify · 2026-10-26 20:30
**Pergunta:** Q2 — Criar conta com um e-mail que já existe por conta Google (usuário sem senha): o que fazer?
**Opções:** A: Recusar e orientar (Recommended) (recomendada) · B: Vincular senha à conta existente · C: Recusar sem detalhar
**Escolha:** A: Recusar e orientar (Recommended)
**Por quê:** A opção A está marcada como recomendada, então segue a política. Ela também evita o risco de segurança de vincular senha a uma conta Google sem verificar o e-mail, além de não ampliar o escopo.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-26 20:34
**Pergunta:** Quando alguém entra com Google usando um e-mail que já tem conta com senha (criada sem verificação de e-mail, possivelmente por outra pessoa), o que deve acontecer com a senha existente? Por que importa: sem verificação de e-mail, quem cadastrou o e-mail de outra pessoa continuaria entrando com a senha depois que a dona real entrar pelo Google (sequestro de conta pré-criada).
**Opções:** Reutilizar e apagar senha (Recommended) (recomendada) · Reutilizar e manter senha · Recusar o login Google
**Escolha:** Reutilizar e apagar senha (Recommended)
**Por quê:** A opção 1 está marcada como recomendada e fecha o risco de sequestro de conta pré-criada, já que o Google comprova a posse do e-mail. Não amplia o escopo de forma relevante: apenas remove a senha existente ao reutilizar a conta.
_claude-code / claude-sonnet-5-5_

## clarify · 2026-10-26 20:34
**Pergunta:** Onde fica o formulário de "Criar conta" no frontend? Por que importa: define rotas, redirecionamento pós-cadastro (FR-011), links compartilháveis e os testes unitários da tela de entrada (FR-018).
**Opções:** Rota própria /cadastro (Recommended) (recomendada) · Abas na tela de entrada · Alternar no mesmo card
**Escolha:** Rota própria /cadastro (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Uma rota própria /cadastro também mantém cada tela simples e dá URL direta para o redirecionamento pós-cadastro.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-26 20:49
**Pergunta:** Quer que eu sugira edições concretas para os principais problemas? Nada será aplicado sem a sua aprovação.
**Opções:** Top 5 (Recommended) (recomendada) · Só o crítico · Todos · Nenhum
**Escolha:** Top 5 (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela cobre os principais achados sem ampliar o escopo para todos os 14. Nada é aplicado sem aprovação, então o risco é baixo.
_claude-code / claude-sonnet-5-5_

## analyze · 2026-10-26 20:56
**Pergunta:** Quer que eu sugira edições concretas para os principais problemas restantes? Nada será aplicado sem a sua aprovação.
**Opções:** Top 5 MEDIUM (Recommended) (recomendada) · Só governança · Todos · Nenhum
**Escolha:** Top 5 MEDIUM (Recommended)
**Por quê:** A opção 1 está marcada como recomendada, então a política manda escolhê-la. Ela também limita as sugestões aos principais achados, sem ampliar o escopo. Nada é aplicado sem aprovação do usuário.
_claude-code / claude-sonnet-5-5_

## tasks · baseline (T001 — 2026-10-26, execução `/speckit.implement`)

Linha de base registrada **antes** de qualquer edição, para não confundir falha pré-existente com
regressão desta feature.

| Verificação | Comando | Resultado |
|---|---|---|
| Backend — compilação por projeto | `dotnet build <proj>.csproj` (DTO, Domain, Infra.Interfaces, Infra, Application, ApiTests, Tests) | **OK nos 7** (sem erro) |
| Backend — `dotnet build` da solução | `cd backend; dotnet build` | **Falha de cópia** — ver nota 1 |
| Frontend — lint | `npm run lint` | **OK** (0 erros, 0 avisos) |
| Frontend — type-check | `npm run type-check` | **OK** |
| Frontend — testes | `npm test -- --run` | **OK — 19 arquivos, 176 testes passando** |
| API em Development no ar | `GET http://localhost:5000/swagger/index.html` | **200** |
| Dev-login disponível (será removido na US3) | `POST /auth/dev-login` | **200** |

**Nota 1 (pré-existente, não é regressão):** a API está em execução em `localhost:5000`
(processo `Ambrosio.API.exe`, PID 17864) e segura as DLLs de `Ambrosio.API/bin`, de modo que o
`dotnet build` da solução inteira falha com `MSB3026/MSB3027` (arquivo bloqueado) ao copiar as
referências. As compilações por projeto passam. **Consequência prática:** cada checkpoint de build
desta feature é feito com `dotnet build <projeto>.csproj` nos projetos afetados; o build da
solução inteira (T057, T079) exige parar a API antes — e reiniciá-la para rodar os testes de API.

**Warnings pré-existentes (não introduzidos pela feature):**
`OrderService.cs(102)` CS8601; `Ambrosio.Tests/.../CpfTest.cs` CS8602 (4×);
`OrderInsertInfoValidatorTest.cs(375)` xUnit1025; `OrderServiceTest.cs(267)` xUnit2013.
A §VIII manda não ajustar `Ambrosio.Tests` — nada é tocado.

## tasks · inventário dev-login (T002 — checklist da Phase 6 / US3)

`git grep -l -i "dev-login\|devLogin\|DevLoginForm\|VITE_DEV_LOGIN\|devEmail\|devName\|AuthProvider.Dev"`
fora de `specs/` → **48 arquivos**. Distribuição e tarefa que resolve cada um:

**Backend — código (remover/renomear)**
1. `backend/Ambrosio.DTO/Enums/AuthProvider.cs` — T003 (`Dev = 2` → `Password = 2`)
2. `backend/Ambrosio.DTO/Auth/DevLoginInfo.cs` — T052 (apagar)
3. `backend/Ambrosio.Domain/Validators/DevLoginInfoValidator.cs` — T052 (apagar)
4. `backend/Ambrosio.Domain/Interfaces/IAuthService.cs` — T052 (remover `DevLoginAsync`)
5. `backend/Ambrosio.Domain/Services/AuthService.cs` — T003/T052
6. `backend/Ambrosio.Domain/Models/User.cs` — T004 (XML doc cita dev-login)
7. `backend/Ambrosio.API/Controllers/AuthController.cs` — T053 (apagar ação + `IHostEnvironment`)
8. `backend/Ambrosio.API/Swagger/DevLoginDocumentFilter.cs` — T054 (apagar)
9. `backend/Ambrosio.API/Extensions/HostEnvironmentExtensions.cs` — T055 (apagar se `Homolog` ficar sem uso)
10. `backend/Ambrosio.API/Program.cs` — T054 (sem `DocumentFilter`, descrição do Bearer)

**Backend — testes**
11. `backend/Ambrosio.ApiTests/appsettings.Test.json` — T045
12. `backend/Ambrosio.ApiTests/Fixtures/ApiTestFixture.cs` — T046
13. `backend/Ambrosio.ApiTests/Controllers/AuthControllerTests.cs` — T049
14. `backend/Ambrosio.ApiTests/Controllers/UsersControllerTests.cs` — T047
15. `backend/Ambrosio.ApiTests/Controllers/UnauthorizedAccessTests.cs` — T048
16. `backend/Ambrosio.Tests/Domain/Services/AuthServiceTest.cs` — T003 (APAGAR o arquivo)
17. `backend/Ambrosio.Tests/Domain/Validators/DevLoginInfoValidatorTest.cs` — T003 (APAGAR o arquivo)
18. `backend/Ambrosio.Tests/Domain/Services/UserServiceTest.cs` — T003 (apagar os casos de `AuthProvider`)

**Frontend**
19. `frontend/src/pages/DevLoginForm.tsx` — T058 (apagar)
20. `frontend/src/pages/LoginPage.tsx` — T058 / T044
21. `frontend/src/pages/LoginPage.test.tsx` — T034 / T058
22. `frontend/src/Services/authService.ts` — T059
23. `frontend/src/Services/apiClient.test.ts` — T059
24. `frontend/src/Contexts/AuthContext.tsx` — T059
25. `frontend/src/types/auth.ts` — T059
26. `frontend/src/i18n/locales/pt-BR.ts` — T060
27. `frontend/src/vite-env.d.ts` — T061
28. `frontend/.env.example` / 29 `.env.development` / 30 `.env.homolog` / 31 `.env.production` — T061
32. `frontend/Dockerfile` — T063
33. `frontend/scripts/verify-prod.mjs` — T062
34. `frontend/README.md` — T069

**Bruno / compose**
35. `bruno/Auth/Dev Login.bru` — T067 (apagar)
36. `bruno/collection.bru` — T068
37. `bruno/environments/Homolog.bru` — T068
38. `docker-compose.yml` — T064
39. `docker-compose-prod.yml` — T064

**Documentação / governança**
40. `backend/README.md` — T069
41. `backend/scripts/seed-load-test.sql` — T069
42. `deploy/README.md` — T069
43. `docs/README.md` — T070
44. `docs/historias/001-api-cadastro-clientes-pedidos.md` — T070
45. `docs/historias/002-frontend-web-perfil-cliente-pedidos.md` — T070
46. `docs/historias/007-oauth-mcp-chatgpt.md` — T070
47. `docs/historias/009-bot-migracao-mais-vendas.md` — T070
48. `MAESTRO.md` — T071

**Fora do grep de código-fonte, mas coberto:** `.specify/memory/constitution.md` (linhas 35 e 230)
— T072 aplica a emenda; `specs/001-.../contracts/openapi.yaml` — T024/T040/T057.

## Setup · arquivos de ignore (etapa 4 do `/speckit.implement`)

Verificados/criados em 2026-10-26: `.gitignore` (raiz), `backend/.gitignore`,
`frontend/.gitignore`, `.dockerignore` (backend e frontend) já continham os padrões críticos da
stack (C#/.NET: `bin/ obj/ *.user *.suo packages/`; Node/TS: `node_modules/ dist/ coverage/
*.log .env*`). **Nenhuma outra ferramenta presente**: sem `.eslintrc*` clássico (usa
`frontend/eslint.config.js` flat config, cujas `ignores` já cobrem `dist/`, `node_modules/`,
`test-results/`, `coverage/`), sem Prettier, sem Terraform, sem charts Helm, e `frontend/package.json`
é `"private": true` (não é publicado → sem `.npmignore`). Nada foi criado.

## tasks · follow-up §VIII (T056 — 2026-10-26)

`git grep -n "AuthProvider\|DevLogin\|AuthService" -- backend/Ambrosio.Tests` → **1 ocorrência**:
`Domain/Services/UserServiceTest.cs:14`, dentro do `<summary>` que explica que os casos de
`AuthProvider` foram apagados. É prosa, não código — nenhum teste unitário remanescente
**referencia** a autenticação.

Conferido no diff: `AuthServiceTest.cs` e `DevLoginInfoValidatorTest.cs` foram **apagados
inteiros**; em `UserServiceTest.cs` saíram só o helper `NewUser()`, o caso
`GetByIdAsync_ShouldMapProfileWithoutPassword` e o `using Ambrosio.DTO.Enums` (os três dependiam
de `AuthProvider.Dev`). O caso sobrevivente, `UserInfo_ShouldNotExposeAnyPasswordProperty`, é
pré-existente (HU 002, T030) e usa reflexão sobre o DTO — não toca autenticação. Nenhum teste
unitário do backend foi criado, ajustado ou executado nesta feature (constituição §VIII).

**Follow-up — remover o projeto `Ambrosio.Tests` inteiro, em ciclo próprio (fora do escopo da
003):** a §VIII determina que o backend é coberto somente por testes de API. O projeto é um
desvio pré-existente que esta feature **reduziu** (cobertura de autenticação foi a zero) mas não
eliminou. A remoção deve levar junto o `.csproj`, a pasta, a referência na solution e qualquer
etapa de pipeline que o execute — e elimina de uma vez os warnings pré-existentes registrados no
baseline que vivem nele (`Domain/ValueObjects/CpfTest.cs` CS8602 ×5,
`Domain/Validators/OrderInsertInfoValidatorTest.cs(375)` xUnit1025,
`Domain/Services/OrderServiceTest.cs(267)` xUnit2013). Não fazer agora: apagá-lo mudaria o
escopo do PR e a §VIII pede que o desvio seja reduzido, não que a remoção aconteça de carona.

## tasks · medições de desempenho e SC-007 (T076/T077 — 2026-10-26, Homolog)

Medidas contra a homologação **reconstruída** no ciclo (`http://localhost:8080/api`), com
`Invoke-WebRequest` a partir do Windows. 20–30 chamadas por caso.

**Armadilha de medição (registrada para não se repetir):** a primeira leva foi feita em **blocos
sequenciais** — 20 logins válidos, depois 20 senhas erradas, depois 20 e-mails inexistentes — e
produziu tempos decrescentes (p95 351,8 → 337,5 → 267,0 ms). Lido de frente, isso parecia um
vazamento de 26% entre os dois caminhos de 401, ou seja, uma violação de SC-006. Era
**aquecimento** (JIT, pool de conexão HTTP, primeira derivação PBKDF2), não assimetria: refeito
com aquecimento descartado **e chamadas intercaladas**, a diferença desaparece. Medir em blocos
um critério de tempo uniforme é o jeito de fabricar um falso positivo.

### T076 — tempo de resposta (Performance Goals: p95 ≤ 500 ms)

Todos os valores em ms, após aquecimento e com chamadas intercaladas.

| Caso | n | min | mediana | média | p95 | max |
|---|---|---|---|---|---|---|
| `POST /auth/register` (201) | 20 | 160,1 | 183,4 | 187,6 | **209,3** | 221,4 |
| `POST /auth/login` válido (200) | 20 | 162,5 | 186,0 | 187,1 | **214,4** | 220,6 |
| `POST /auth/login` válido (200) | 30 | 181,8 | 216,6 | 230,8 | **300,3** | 309,5 |
| `POST /auth/login` senha errada (401) | 30 | 140,8 | 175,5 | 171,4 | **197,2** | 217,4 |
| `POST /auth/login` e-mail inexistente (401) | 30 | 140,1 | 176,6 | 171,7 | **206,0** | 212,7 |

**p95 de 209–300 ms, todos ≤ 500 ms ✅.** O custo do PBKDF2 (600.000 iterações) aparece como
~170–215 ms por chamada, dentro do previsto no research §1/§2. Os 448,6 ms medidos no primeiro
`/auth/register` eram partida a frio, não o regime estável.

**SC-006 (tempo equivalente entre os dois 401):** diferença de **mediana** entre senha errada e
e-mail inexistente = **1,1 ms (0,6%)**; diferença de p95 = 8,8 ms; as faixas se sobrepõem quase
integralmente (140,8–217,4 vs 140,1–212,7). Ambos devolvem **401** com corpo idêntico:
`{"sucesso":false,"mensagem":"E-mail ou senha inválidos","erros":[]}`. Coerente com o desenho —
exatamente **uma** verificação PBKDF2 nos dois caminhos (contra o hash do usuário, ou contra o
`DummyHash` quando o e-mail não existe / a conta não tem senha). **SC-006 ✅**

### T077 — SC-007 (nenhuma resposta expõe senha)

A inspeção tem de ser por **chave** JSON, não por substring: desde a 003 o valor de
`user.provider` é literalmente `"password"`, então um `NotContain("password")` falharia em toda
resposta autenticada sem que nenhum segredo tivesse vazado. (Caí exatamente nesse falso positivo
na primeira medição; o helper `Ambrosio.ApiTests/Helpers/AuthBodyAssertions.cs` já estava escrito
do jeito certo, percorrendo o JSON recursivamente pelos nomes de propriedade.)

Contagem de chaves `"password"` / `"passwordHash"` nos corpos reais observados em Homolog:

| Resposta | chave `password` | chave `passwordHash` |
|---|---|---|
| `POST /auth/register` (201) | 0 | 0 |
| `POST /auth/login` (200) | 0 | 0 |
| `GET /users/me` (200) | 0 | 0 |
| `POST /auth/google` (400, ID token inválido) | 0 | 0 |

Corpos: `LoginResult` = `{ token, expiresAt, user: { id, email, name, provider, createdAt } }`;
`/users/me` = `{ id, email, name, provider, createdAt }`.

**`/auth/google` em 200 não foi observado** — exige ID token de uma conta Google real, que não
está disponível nesta execução (o Cenário 7 do quickstart é manual). Foi verificado pelo
**contrato declarado**, que é o mesmo corpo das outras duas rotas: no Swagger de Homolog,
`LoginResult` = `token, expiresAt, user` e `UserInfo` = `id, email, name, provider, createdAt` —
nenhuma propriedade de senha; as únicas chaves `password` do documento inteiro são as de entrada
`RegisterInfo.password` e `LoginInfo.password`, ambas `writeOnly: true` no
`specs/001-.../contracts/openapi.yaml`.

Cobertura automatizada: `AssertNoPasswordKey` em `AuthControllerTests.cs:55` (register) e `:237`
(login), e `AuthBodyAssertions.ShouldHaveNoPasswordKey` em `UsersControllerTests.cs:34`
(`/users/me`). **SC-007 ✅** (com a ressalva do Google em 200, coberto por schema).

## tasks · T075 bloqueado e T078 pendente de verificação manual (2026-10-26)

Decisões do usuário nesta execução: T075 registrado como bloqueado (sem instalar o CLI do Bruno),
T078 verificado manualmente por ele, e **nenhum commit** feito — o ciclo inteiro ficou no working
tree para revisão.

### T075 — coleção do Bruno contra Homolog: **NÃO EXECUTADO (bloqueado)**

Dois impeditivos concretos:

1. O CLI do Bruno não está instalado nesta máquina — `bru --version` devolve
   `'bru' não é reconhecido como um comando interno ou externo`.
2. A coleção depende da variável **secreta** `userPassword` do ambiente Homolog, cujo valor não
   está disponível aqui. Como `homolog@ambrosio.test` já existe no banco de Homolog, "Criar conta"
   responderia 409 e "Login com senha" precisaria da senha real; sem token, Users/Customers/Orders
   falhariam em cascata.

**O que fica pendente:** SC-008. Os arquivos da coleção foram criados e ajustados conforme
T066–T068 (`bruno/Auth/Criar conta.bru` seq 1, `bruno/Auth/Login com senha.bru` seq 2,
`Google Login.bru` remanejado para seq 3, `Dev Login.bru` apagado, `userEmail`/`userName` em
`vars` e `userPassword` em `vars:secret` nos dois arquivos de ambiente/collection) — falta só a
execução.

**Mitigação obtida por outra via:** os endpoints que a coleção exercita estão cobertos pelos
**99 testes de API rodados duas vezes contra Homolog** nesta rodada (T074, 100% verdes), incluindo
o encadeamento cadastro → login → `/users/me` → clientes → pedidos e o isolamento entre os
usuários A e B. O que a suíte não reproduz é o formato `.bru` em si: a ordem `seq`, o
`script:post-response` gravando `token` e o `vars:secret`.

**Para destravar:** `npm i -g @usebruno/cli` (ou `npx @usebruno/cli`), preencher `userPassword` no
ambiente Homolog e rodar `cd bruno; bru run --env Homolog`. Esperado: "Criar conta" 201 na
primeira execução e 409 nas seguintes; "Login com senha" 200 gravando `token`; demais requisições
passando com esse token.

### T078 — Cenário 4 do quickstart: **ABERTO**, verificação manual pelo usuário

**Já coberto** pelos 235 testes unitários do frontend (Vitest + RTL, todos verdes): navegação para
`state.returnTo` e para `/clientes` quando não há returnTo; redirecionamento de `/cadastro` com
sessão ativa; alternância Mostrar/Ocultar com `aria-pressed`; mensagens de validação no campo;
409/401 em `role="alert"`; senha preservada no campo após erro; link "Já tenho conta" → `/login`
carregando o returnTo; botão Google e separador "ou" ocultos sem `VITE_GOOGLE_CLIENT_ID`.

**Já medido por mim:** bundle inicial **99,65 kB gzip** (entry `index-*.js`) + 6,72 kB de CSS —
bem abaixo do orçamento de 250 kB gzip do plan.md ✅.

**Não coberto por teste nenhum — precisa de olho humano:** ausência de rolagem horizontal a
360 px e foco visível ao percorrer os campos com `Tab` (FR-019/FR-020).

**Cenário 7 (FR-009, Google em conta com senha):** não executado — exige
`AUTH_GOOGLE_CLIENT_ID`/`VITE_GOOGLE_CLIENT_ID` configurados e uma conta Google real. A regra
`ClearPassword` está implementada em `AuthService.LoginWithGoogleAsync` (T038) mas só o teste
manual a valida de ponta a ponta.

## tasks · o checkpoint da Phase 6 é autocontraditório (T079 — 2026-10-26)

O checkpoint da Phase 6 e o Cenário 8 do quickstart pedem

```powershell
git grep -n -i "dev-login\|devLogin\|VITE_DEV_LOGIN\|devEmail" -- . ":!specs/" ":!backend/.tmp-stage"
```

com **"nenhuma ocorrência fora de `specs/`"**. Isso é **inatingível pelo desenho da própria
feature**: quatro tarefas obrigatórias exigem que os termos apareçam literalmente fora de
`specs/`. Não é resíduo esquecido — é o texto prescrito.

Classificação das 28 ocorrências remanescentes:

| Onde | n | Por que precisa existir |
|---|---|---|
| `backend/Ambrosio.ApiTests/Controllers/AuthControllerTests.cs` | 7 | **T051** manda criar `DevLogin_ShouldReturn404InAnyEnvironment` (que faz `POST /auth/dev-login`) e o teste do Swagger que assevera `NotContain("dev-login")` / `NotContain("DevLoginInfo")`. Uma prova de ausência precisa nomear o que está provando ausente. |
| `docs/historias/001` + `002` | 10 | **T070** diz "acrescentar nota **sem reescrever o histórico**": as linhas de tabela, o diagrama e as perguntas em aberto das HU 001/002 descrevem o que era verdade na época e ficam como estavam. |
| `.specify/memory/constitution.md` | 5 | **T072** aplica `constitution-amendment.md` à risca: a regra nova cita "(ex.: o antigo `POST /auth/dev-login`)" para proibi-lo nominalmente, e o Sync Impact Report + a nota histórica da emenda 3.0.0 registram a mudança. Proibir por nome exige o nome. |
| `docs/historias/001`/`002`/`007` — notas novas | 5 | Mesma **T070**: a própria nota "isto não existe mais" menciona o termo que declara extinto (2 linhas na 001, 2 na 002, 1 na 007). |
| `MAESTRO.md:33` | 1 | **T071** prescreve o comentário literal: "falha se dev-login/DevLoginForm/VITE_DEV_LOGIN sobreviver no `dist/`". |

**O objetivo substantivo está atendido**, e é esse o que importa: `git grep` em `frontend/` →
**0 ocorrências**; em `backend/` fora do arquivo de teste que prova a ausência → **0**;
`docker-compose*.yml`, `frontend/Dockerfile`, `.env.*`, `vite-env.d.ts`, `src/`, `Program.cs`,
`AuthController.cs`, DTOs e validators → **0**. Não sobrou nenhum caminho, flag, DTO, formulário
ou argumento de build que emita token sem credencial — confirmado também em execução: 404 com
corpo vazio em Development **e** Homolog, e o `swagger.json` de Homolog não contém nem o caminho
nem o schema. O `verify:prod` ainda procura esses termos no `dist/` montando-os por
concatenação, justamente para não se auto-denunciar no grep.

**Recomendação (não aplicada — é artefato de aceitação da feature, decisão sua):** reescrever o
Cenário 8 para um critério realizável, por exemplo excluir os três casos legítimos

```powershell
git grep -n -i "dev-login\|devLogin\|VITE_DEV_LOGIN\|devEmail" -- . ":!specs/" ":!backend/.tmp-stage" `
  ":!backend/Ambrosio.ApiTests/Controllers/AuthControllerTests.cs" ":!docs/historias/" `
  ":!MAESTRO.md" ":!.specify/memory/constitution.md"
```

e/ou inverter a afirmação: "nenhuma ocorrência em `frontend/` nem em `backend/` fora do teste que
prova o 404". Do jeito atual, um próximo ciclo pode "corrigir" esse checkpoint apagando os testes
que garantem a remoção — o pior resultado possível.



