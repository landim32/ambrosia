# Feature Specification: Login com e-mail e senha e criação de conta

**Feature Branch**: `003-email-password-jwt-auth`

**Created**: 2026-10-26

**Status**: Draft

**Input**: User description: "Alterar o login: remover o endpoint /auth/dev-login de todos os ambientes; adicionar o login com email e senha, usando JWT; adicionar um endpoint para criar conta; no fluxo de teste e2e do backend deve criar o usuário e logar na sequência; alterar o frontend para suportar isso; alterar os endpoints no Bruno também"

## Contexto

Hoje o Ambrosia aceita duas formas de entrada: a conta Google (`/auth/google`) e a **entrada de desenvolvimento** (`/auth/dev-login`), que emite uma credencial sem senha a partir de e-mail e nome e existe apenas em Development e Homolog. A entrada de desenvolvimento é usada pelos testes de API do backend, pelo formulário de desenvolvimento do frontend e pela coleção do Bruno.

Esta feature **substitui a entrada de desenvolvimento por um cadastro real**: a vendedora cria uma conta com e-mail e senha, entra com essas credenciais e recebe a mesma credencial de sessão (token) que o login Google já emite. A entrada de desenvolvimento deixa de existir em **todos** os ambientes, inclusive Development e Homolog, e tudo que dependia dela (testes de API, frontend, Bruno) passa a usar o cadastro + login com senha. O login com Google é mantido.

## Clarifications

### Session 2026-10-26

- Q: O login com Google deve ser mantido ao lado do login com e-mail e senha? → A: Sim; a tela de entrada oferece o formulário de e-mail/senha e o botão Google.
- Q: Criar conta com e-mail que já existe por conta Google (sem senha): o que fazer? → A: Recusar com "Este e-mail já está cadastrado" e orientar a entrar com Google; a conta Google continua sem senha.
- Q: Quando alguém entra com Google usando um e-mail que já tem conta com senha, o que acontece com a senha existente? → A: A conta é reutilizada e a senha existente é removida (o Google comprova a posse do e-mail); a partir daí só o Google entra, até uma futura recuperação/troca de senha.
- Q: Onde fica o formulário de "Criar conta" no frontend? → A: Em tela própria, na rota `/cadastro`, com link "Criar conta" na tela de entrada e link "Já tenho conta" de volta; a tela de origem é preservada para o redirecionamento pós-cadastro.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Criar uma conta com e-mail e senha (Priority: P1)

A vendedora que ainda não tem conta abre a tela de entrada, segue o link "Criar conta" para a tela de cadastro (`/cadastro`), informa nome, e-mail e senha (com confirmação) e já entra no sistema conectada, sem precisar de uma conta Google.

**Why this priority**: sem cadastro não existe usuário para entrar com senha; é o que torna a entrada de desenvolvimento dispensável e o pré-requisito de todas as demais histórias.

**Independent Test**: na tela de entrada, criar uma conta com um e-mail novo e confirmar que o cabeçalho mostra o nome e e-mail informados e que a lista de clientes abre vazia.

**Acceptance Scenarios**:

1. **Given** um e-mail ainda não cadastrado, **When** a vendedora informa nome, e-mail e uma senha válida (duas vezes iguais) e confirma, **Then** a conta é criada, a sessão é iniciada imediatamente e ela vê a lista de clientes com seu nome e e-mail no cabeçalho.
2. **Given** um e-mail já cadastrado com senha, **When** tenta criar conta com ele, **Then** vê a mensagem "Este e-mail já está cadastrado" e nenhuma conta nova é criada.
3. **Given** um e-mail já cadastrado apenas por conta Google (sem senha), **When** tenta criar conta com ele, **Then** vê "Este e-mail já está cadastrado. Entre com sua conta Google." e nenhuma senha é definida para a conta existente.
4. **Given** uma senha fora da política (menos de 8 caracteres) ou confirmação diferente, **When** tenta criar conta, **Then** vê a mensagem do campo inválido e o formulário não é enviado.
5. **Given** um e-mail mal formado ou nome vazio, **When** tenta criar conta, **Then** vê a mensagem do campo inválido e nada é enviado.
6. **Given** o mesmo e-mail digitado com maiúsculas ou espaços nas pontas, **When** cria a conta, **Then** o e-mail é tratado como o mesmo e-mail normalizado (não gera conta duplicada).
7. **Given** a vendedora foi enviada à tela de entrada ao tentar abrir uma tela protegida, **When** segue "Criar conta" e conclui o cadastro, **Then** é levada à tela que tentou abrir originalmente; o link "Já tenho conta" a devolve à tela de entrada mantendo esse destino.

---

### User Story 2 - Entrar com e-mail e senha (Priority: P1)

A vendedora com conta criada abre a tela de entrada, informa e-mail e senha e passa a ver apenas os seus dados, com a mesma sessão (duração, expiração, "Sair") que já existe para o login Google.

**Why this priority**: é o uso diário; sem ele a conta criada na US1 não serve para nada.

**Independent Test**: com uma conta criada, sair, entrar novamente com e-mail e senha e confirmar que os dados são os mesmos da conta; tentar com senha errada e confirmar a recusa.

**Acceptance Scenarios**:

1. **Given** uma conta existente, **When** informa e-mail e senha corretos, **Then** a sessão é iniciada e ela retorna à tela que tentou abrir originalmente (ou à lista de clientes).
2. **Given** uma conta existente, **When** informa senha incorreta, **Then** vê "E-mail ou senha inválidos" e permanece na tela de entrada.
3. **Given** um e-mail não cadastrado, **When** tenta entrar, **Then** vê a **mesma** mensagem "E-mail ou senha inválidos" (sem revelar se o e-mail existe).
4. **Given** uma conta criada apenas por Google (sem senha), **When** tenta entrar com e-mail e senha, **Then** vê a mesma mensagem genérica "E-mail ou senha inválidos".
5. **Given** uma sessão iniciada por senha, **When** o prazo expira ou a vendedora aciona "Sair", **Then** o comportamento é idêntico ao da sessão iniciada por Google (aviso de expiração, volta à tela de entrada, nada fica no navegador).
6. **Given** a tela de entrada com a conta Google configurada, **When** a tela abre, **Then** o botão "Entrar com Google" continua disponível junto ao formulário de e-mail e senha, e entrar por Google funciona como antes.
7. **Given** uma conta com senha, **When** alguém entra com Google usando o mesmo e-mail, **Then** a sessão é iniciada na conta existente (mesmos dados) e a senha da conta é removida; uma tentativa posterior de entrar com aquela senha recebe "E-mail ou senha inválidos".

---

### User Story 3 - Entrada de desenvolvimento desaparece de todos os ambientes (Priority: P1)

Ninguém — nem em Development, nem em Homolog, nem em Production — consegue obter uma credencial sem senha. O endpoint, o formulário do frontend, a flag de build, a requisição do Bruno e as referências na documentação são removidos.

**Why this priority**: é o requisito de segurança central do pedido; manter qualquer resquício deixaria uma porta sem senha em ambientes com dados reais de homologação.

**Independent Test**: em cada ambiente, chamar `/auth/dev-login` e receber "não encontrado"; inspecionar o pacote do frontend (qualquer modo de build) e o Swagger e não encontrar referência alguma à entrada de desenvolvimento.

**Acceptance Scenarios**:

1. **Given** qualquer ambiente, **When** uma requisição é feita a `/auth/dev-login`, **Then** a resposta é "não encontrado", indistinguível de uma rota inexistente.
2. **Given** a documentação interativa da API (Swagger) em qualquer ambiente, **When** é aberta, **Then** não lista a entrada de desenvolvimento.
3. **Given** o pacote do frontend gerado em qualquer modo (development, homolog, production), **When** é inspecionado, **Then** não contém o formulário de desenvolvimento nem chamadas a `/auth/dev-login`; a verificação automatizada existente (`verify:prod`) continua passando e passa a cobrir todos os modos.
4. **Given** a coleção do Bruno, **When** é aberta, **Then** não existe mais a requisição "Dev Login" e existem as requisições "Criar conta" e "Login com senha", que gravam o token no ambiente para as demais requisições; "Google Login" permanece.

---

### User Story 4 - Testes de API criam o usuário e entram na sequência (Priority: P2)

Quem roda os testes de API do backend contra Development ou Homolog não precisa de nenhum usuário pré-existente: a própria suíte cria os usuários de teste (com e-mail único por execução) pelo cadastro e entra com senha logo em seguida, obtendo os tokens que os testes usam.

**Why this priority**: sem isso a suíte inteira fica sem credencial depois da remoção da entrada de desenvolvimento; é o que mantém a regra da constituição (ciclo termina com testes de API passando contra Homolog).

**Independent Test**: rodar a suíte completa de testes de API contra Homolog recém-construída, sem nenhum usuário cadastrado manualmente, e obter 100% de aprovação.

**Acceptance Scenarios**:

1. **Given** a API no ar e sem usuários de teste, **When** a suíte inicia, **Then** cria o usuário A e o usuário B pelo cadastro, entra com cada um pelo login com senha e todos os testes que exigem credencial passam.
2. **Given** a suíte em execução, **When** um teste precisa de um usuário extra, **Then** consegue criá-lo e autenticá-lo pelo mesmo caminho (cadastro + login), sem atalhos.
3. **Given** a suíte, **When** executada, **Then** cobre também os cenários de cadastro (sucesso, e-mail duplicado, senha fraca) e de login (sucesso, senha errada, e-mail inexistente, sem credencial) e confirma que `/auth/dev-login` responde "não encontrado".
4. **Given** duas execuções consecutivas da suíte contra o mesmo banco, **When** a segunda roda, **Then** não falha por e-mail duplicado (e-mails únicos por execução).

---

### Edge Cases

- **E-mail com variação de caixa/espaços** (`Ana@Exemplo.com ` vs `ana@exemplo.com`): tratado como o mesmo e-mail no cadastro e no login.
- **Senha com espaços nas pontas**: preservada como digitada (espaços contam); a vendedora é a responsável pelo que digita.
- **Senha muito longa** (ex.: 1.000 caracteres): aceita até um limite razoável (128 caracteres) e recusada acima dele com mensagem clara.
- **Cadastro simultâneo do mesmo e-mail** (duas requisições concorrentes): apenas uma conta é criada; a outra recebe "e-mail já cadastrado".
- **Tentativas repetidas de login com senha errada**: respondem sempre a mesma mensagem genérica e no mesmo tempo aproximado, sem revelar se o e-mail existe. Bloqueio por tentativas fica fora do escopo (ver Assumptions).
- **Sessão antiga obtida por dev-login ainda dentro do prazo**: continua válida até expirar, pois a credencial em si não muda; nenhuma invalidação retroativa é exigida.
- **Usuários antigos criados por dev-login** no banco de Homolog/Development: permanecem, sem senha; não conseguem entrar por senha nem criar conta com o mesmo e-mail (tratados como "já cadastrado"). São descartados ao reconstruir a homologação.
- **Conta Google tenta criar conta com senha**: recusada com orientação para entrar com Google (US1 cenário 3). **Conta com senha e alguém entra com Google usando o mesmo e-mail**: o login Google reutiliza a conta existente e **remove a senha** (o Google comprova a posse do e-mail; evita que quem pré-cadastrou o e-mail de outra pessoa continue entrando). Sessões já emitidas por senha continuam válidas até expirar (sem invalidação retroativa).
- **Vendedora já conectada abre `/cadastro`**: é levada à lista de clientes, como já acontece com a tela de entrada.
- **Erro da API ou falha de rede durante cadastro/login**: aviso curto com a mensagem retornada ou "Não foi possível conectar", com possibilidade de tentar de novo; a senha digitada não é perdida do formulário.
- **Ambiente sem conta Google configurada**: a tela de entrada funciona só com e-mail e senha, sem botão Google quebrado.

## Requirements *(mandatory)*

### Functional Requirements

**Cadastro**

- **FR-001**: O sistema DEVE oferecer a criação de conta com nome, e-mail e senha, sem exigir conta Google.
- **FR-002**: O sistema DEVE recusar cadastro com e-mail já existente (comparação pelo e-mail normalizado: minúsculas, sem espaços nas pontas), com a mensagem "Este e-mail já está cadastrado".
- **FR-003**: O sistema DEVE validar no cadastro: nome obrigatório (1 a 120 caracteres), e-mail obrigatório e bem formado (até 254 caracteres), senha obrigatória com 8 a 128 caracteres. Mensagens de validação DEVEM indicar o campo inválido.
- **FR-004**: Ao criar a conta com sucesso, o sistema DEVE iniciar a sessão imediatamente, devolvendo a mesma credencial de sessão (token com prazo de expiração e dados básicos do usuário) que o login devolve.
- **FR-005**: O sistema NUNCA DEVE armazenar a senha em texto puro nem devolvê-la (ou seu hash) em qualquer resposta ou tela.
- **FR-006**: Quando o e-mail já existe apenas por conta Google, o sistema DEVE recusar o cadastro com "Este e-mail já está cadastrado. Entre com sua conta Google." e NÃO DEVE definir senha na conta existente.

**Login**

- **FR-007**: O sistema DEVE permitir entrar com e-mail e senha, devolvendo a mesma credencial de sessão do login Google (mesmo formato, mesmo prazo padrão, mesma identificação do usuário).
- **FR-008**: Em e-mail inexistente, senha incorreta ou conta sem senha (só Google), o sistema DEVE responder a mesma mensagem genérica "E-mail ou senha inválidos", sem distinguir os casos.
- **FR-009**: O login com conta Google DEVE ser mantido, funcionando como hoje, ao lado do login com senha. Quando o e-mail do Google corresponde a uma conta existente, a conta DEVE ser reutilizada; se essa conta tiver senha, a senha DEVE ser removida no mesmo login (a conta passa a entrar só por Google, e a entrada por senha passa a receber a mensagem genérica do FR-008).
- **FR-010**: A sessão iniciada por senha DEVE ter o mesmo ciclo de vida da sessão existente: persistir entre aberturas do navegador até expirar, encerrar com aviso ao expirar ou ser recusada pela API, e "Sair" DEVE remover todo dado guardado no navegador.
- **FR-011**: Após entrar ou criar conta, o sistema DEVE levar a vendedora à tela que tentou abrir originalmente (ou à lista de clientes).

**Remoção da entrada de desenvolvimento**

- **FR-012**: O sistema NÃO DEVE expor `/auth/dev-login` em nenhum ambiente: a rota DEVE responder "não encontrado" como qualquer rota inexistente e NÃO DEVE aparecer na documentação interativa.
- **FR-013**: O frontend NÃO DEVE conter o formulário de desenvolvimento, a flag de build que o controlava nem chamadas à rota removida, em nenhum modo de build; a verificação automatizada do pacote DEVE continuar existindo e passar em todos os modos.
- **FR-014**: A coleção do Bruno DEVE substituir "Dev Login" por "Criar conta" e "Login com senha", ambas gravando o token no ambiente, manter "Google Login", e as variáveis de ambiente da coleção DEVEM refletir as novas credenciais (e-mail, nome e senha de teste) em vez de "devEmail/devName".
- **FR-015**: Documentação do projeto (READMEs, guias de ambiente, constituição) NÃO DEVE mais descrever a entrada de desenvolvimento como forma de acesso.

**Testes de API**

- **FR-016**: A suíte de testes de API DEVE criar seus próprios usuários (e-mail único por execução) pelo cadastro e entrar com senha na sequência, sem depender de usuário pré-existente nem de atalho sem senha.
- **FR-017**: A suíte DEVE cobrir cadastro (sucesso, e-mail duplicado, senha fora da política, campos inválidos), login (sucesso, senha errada, e-mail inexistente, corpo inválido) e a ausência de `/auth/dev-login`, além de manter os testes de isolamento entre usuários A e B já existentes.

**Interface**

- **FR-018**: A tela de entrada DEVE ter o formulário de e-mail e senha, o link "Criar conta" que leva à tela própria de cadastro (rota `/cadastro`, pública) e o botão Google. A tela de cadastro DEVE ter o link "Já tenho conta" de volta à entrada; ambos os links DEVEM preservar a tela de origem para o redirecionamento do FR-011, e quem já está conectado ao abrir `/cadastro` DEVE ser levado à lista de clientes.
- **FR-019**: Os formulários DEVEM mostrar estados de carregando, erro de validação por campo e erro da API, e DEVEM funcionar a partir de 360 px de largura, com rótulos acessíveis e foco visível, em português do Brasil.
- **FR-020**: O campo de senha DEVE ocultar o conteúdo digitado, com opção de mostrar/ocultar.

### Key Entities

- **Usuário (vendedora)**: pessoa que acessa o Ambrosia. Atributos: nome, e-mail (único, normalizado), forma(s) de entrada (senha e/ou Google), segredo de senha (nunca exposto; removido quando a conta entra por Google), datas de criação e atualização. Dona dos clientes e pedidos.
- **Sessão**: credencial emitida ao entrar ou criar conta, com prazo de expiração e dados básicos do usuário; idêntica independentemente da forma de entrada.
- **Credenciais de cadastro**: nome, e-mail, senha informados ao criar conta.
- **Credenciais de login**: e-mail e senha informados ao entrar.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Uma vendedora sem conta cria a conta e vê a lista de clientes em menos de 1 minuto, sem ajuda.
- **SC-002**: Uma vendedora com conta entra com e-mail e senha em menos de 20 segundos a partir da tela de entrada.
- **SC-003**: 100% das requisições a `/auth/dev-login`, em todos os ambientes, respondem "não encontrado"; a documentação interativa não a lista em nenhum ambiente.
- **SC-004**: 0 ocorrências de "dev-login" ou do formulário de desenvolvimento no pacote do frontend em qualquer modo de build, provado pela verificação automatizada.
- **SC-005**: A suíte de testes de API passa 100% contra a homologação reconstruída sem nenhum usuário criado manualmente, em duas execuções consecutivas.
- **SC-006**: 100% das tentativas de login com senha errada, e-mail inexistente ou conta só-Google recebem a mesma mensagem genérica.
- **SC-007**: Nenhuma resposta ou tela expõe a senha ou seu hash (verificado em 100% dos endpoints de autenticação e de usuário).
- **SC-008**: Todas as requisições da coleção do Bruno funcionam contra Homolog a partir de "Criar conta" ou "Login com senha", sem a requisição "Dev Login".
- **SC-009**: O login com Google continua funcionando sem regressão: 100% dos cenários de sessão da spec 002 (US1) passam inalterados.

## Assumptions

- **Cadastro aberto**: qualquer pessoa com um e-mail pode criar conta (não há convite, aprovação nem lista de permitidos), pois o Ambrosia é de uso individual por vendedora e a entrada de desenvolvimento já permitia criar usuários livremente.
- **Sem verificação de e-mail**: o cadastro não envia e-mail de confirmação; o e-mail é aceito como informado. Verificação, recuperação e troca de senha ficam para histórias futuras. Como mitigação do pré-cadastro de e-mail alheio, o primeiro login Google no e-mail remove a senha existente (FR-009).
- **Sem bloqueio por tentativas**: limitação de tentativas de login (rate limiting/lockout) fica fora do escopo; a mitigação nesta entrega é a mensagem genérica e o tempo de resposta uniforme.
- **Política de senha**: mínimo de 8 e máximo de 128 caracteres, sem exigência de composição (maiúsculas, símbolos), para não atrapalhar o uso no celular.
- **Sessão**: mesmo prazo padrão já em uso (8 h) e mesma chave de armazenamento no navegador; nada muda para quem já usa Google.
- **Credencial já emitida por dev-login**: continua válida até expirar; não há invalidação retroativa.
- **Usuários antigos criados por dev-login**: ficam no banco sem senha; a homologação é reconstruída normalmente ao fim do ciclo.
- **Constituição**: o princípio VI (que hoje permite `/auth/dev-login` em Development e Homolog e descreve o esquema como Google-only) precisa de emenda para refletir a nova forma de entrada e a proibição do dev-login em todos os ambientes; a emenda faz parte deste ciclo.
- **Bruno**: as credenciais de teste no ambiente da coleção (e-mail e nome) são variáveis não secretas de homologação local; token e senha ficam como secretas.
- **Testes do frontend**: permanecem apenas unitários (constituição §VIII); os testes de API do backend são os únicos que exercitam o fluxo real de cadastro + login.
- **Testes unitários do backend**: o projeto `Ambrosia.Tests` é um desvio pré-existente da constituição §VIII; esta feature **apaga** os testes unitários afetados pela autenticação, sem ajustá-los nem criar novos, e o comportamento de cadastro/login passa a ser provado só pelos testes de API.
