# Feature Specification: API de cadastro e consulta de clientes e pedidos

**Feature Branch**: `001-api-cadastro-clientes-pedidos`
**Created**: 2026-10-25
**Status**: Draft
**Input**: "Criar a API do Ambrosio em .NET com PostgreSQL, para cadastrar e consultar clientes e pedidos (nos moldes do +Vendas, conforme /docs), com usuários (email, nome, senha), login via OAuth e isolamento de dados por usuário."

## Contexto

O Ambrosio é uma base própria de vendas e clientes de vendedoras, inspirada no sistema +Vendas (docs 01 e 02). Esta primeira entrega é **somente a API**: não há front-end, bot (doc 03) nem MCP (doc 04). A API permite cadastrar e consultar clientes e pedidos (com itens), e cada usuário enxerga apenas os seus próprios dados.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Autenticação e identificação do usuário (Priority: P1)

Uma vendedora se cadastra e entra na API via OAuth. Todas as demais operações exigem estar autenticada e são executadas em nome dela.

**Why this priority**: sem usuário autenticado não há isolamento de dados; é a base de todas as outras histórias.

**Independent Test**: autenticar com um provedor OAuth, obter acesso e consultar o próprio perfil; sem credencial válida, qualquer operação é recusada.

**Acceptance Scenarios**:

1. **Given** uma pessoa sem conta, **When** conclui o login OAuth pela primeira vez, **Then** um usuário é criado com e-mail e nome vindos do provedor e ela recebe acesso.
2. **Given** um usuário já existente, **When** faz login novamente, **Then** entra na mesma conta (sem duplicar, identificada pelo e-mail).
3. **Given** uma requisição sem credencial ou com credencial inválida/expirada, **When** chama qualquer operação de clientes ou pedidos, **Then** a API responde como não autorizada.
4. **Given** um usuário autenticado, **When** consulta o próprio perfil, **Then** vê e-mail e nome, nunca a senha.

---

### User Story 2 - Cadastrar e consultar clientes (Priority: P1)

A vendedora cadastra seus clientes (nome e CPF) e os consulta por listagem, busca e detalhe.

**Why this priority**: o foco do produto é o cliente; pedidos dependem de clientes.

**Independent Test**: cadastrar um cliente, listá-lo, buscá-lo por nome e abrir o detalhe, tudo com um único usuário.

**Acceptance Scenarios**:

1. **Given** um usuário autenticado, **When** cadastra um cliente com nome e CPF válidos, **Then** o cliente fica salvo e vinculado a ele.
2. **Given** um cliente já cadastrado com o mesmo CPF pelo mesmo usuário, **When** tenta cadastrá-lo de novo, **Then** a API recusa informando duplicidade.
3. **Given** vários clientes cadastrados, **When** lista com paginação e busca por texto no nome, **Then** recebe apenas os clientes que correspondem, de forma paginada.
4. **Given** um cliente existente, **When** abre o detalhe, **Then** vê os dados do cliente e um resumo de compras (quantidade de compras, total líquido, data da última compra), conforme `v_customer_summary` do doc 02.
5. **Given** um cliente existente, **When** o usuário atualiza o nome, **Then** a alteração é salva.

---

### User Story 3 - Cadastrar e consultar pedidos com itens (Priority: P1)

A vendedora registra pedidos (venda, devolução ou cancelamento) de um cliente, com seus itens, e os consulta por filtros.

**Why this priority**: é o conteúdo principal que o +Vendas já mantém e que a base própria deve espelhar.

**Independent Test**: cadastrar um pedido de venda com itens para um cliente, consultá-lo por detalhe e listagem, e verificar que a devolução tem total negativo.

**Acceptance Scenarios**:

1. **Given** um cliente do usuário, **When** cadastra um pedido com número, data, tipo, canal, status, total e itens, **Then** o pedido fica salvo com seus itens numerados.
2. **Given** um pedido do tipo devolução ou cancelamento, **When** cadastrado, **Then** o total deve ser negativo; para venda, positivo. Valores fora da regra são recusados.
3. **Given** um pedido cadastrado com número, data e tipo iguais aos de outro do mesmo usuário, **When** tenta cadastrá-lo, **Then** a API recusa por duplicidade. O número do pedido sozinho **não** é chave única (doc 01, ponto 4).
4. **Given** pedidos cadastrados, **When** lista filtrando por período, tipo, canal, cliente ou texto, **Then** recebe apenas os correspondentes, paginados e ordenados por data decrescente.
5. **Given** um pedido existente, **When** abre o detalhe, **Then** vê o cliente, os dados do pedido e todos os itens (nome do produto, referência SKU, tamanho, preço).
6. **Given** um pedido do tipo cancelamento, **When** cadastrado, **Then** pode registrar a data da venda original e a data do cancelamento.
7. **Given** um pedido existente, **When** o usuário altera status ou total (o +Vendas altera vendas até o fechamento), **Then** a alteração é salva.

---

### User Story 4 - Isolamento de dados por usuário (Priority: P1)

Cada usuário só vê e altera os próprios clientes e pedidos.

**Why this priority**: requisito de segurança e privacidade (LGPD, o CPF é dado pessoal).

**Independent Test**: com dois usuários, o usuário B não consegue listar, ler nem alterar nada do usuário A.

**Acceptance Scenarios**:

1. **Given** os usuários A e B com dados próprios, **When** B lista clientes e pedidos, **Then** vê apenas os seus.
2. **Given** um cliente ou pedido do usuário A, **When** B tenta consultá-lo ou alterá-lo pelo identificador, **Then** a API responde "não encontrado", sem revelar que o recurso existe.
3. **Given** o usuário B, **When** tenta cadastrar um pedido associado a um cliente do usuário A, **Then** a API recusa.
4. **Given** o mesmo CPF cadastrado por A e por B, **Then** são dois clientes independentes, cada um visível só ao seu dono.

---

### Edge Cases

- CPF com formato inválido ou dígitos verificadores incorretos: recusado com mensagem clara.
- Pedido sem itens: permitido apenas se o total for informado? (assumido: **pelo menos 1 item é obrigatório**).
- Soma dos preços dos itens diferente do total do pedido: o pedido é aceito, pois o +Vendas pode ter descontos; a divergência não bloqueia.
- Data de pedido no futuro: recusada.
- Paginação com página além do fim: retorna lista vazia.
- Login OAuth cujo provedor não devolve e-mail: o login é recusado.
- Tentativa de excluir cliente que tem pedidos: recusada (sem exclusão em cascata, conforme a constituição).
- Pedidos de canais fora da lista conhecida (Linx, Online, Vitrine, Box, Vintage): aceitos, o canal é texto livre.

## Requirements *(mandatory)*

### Functional Requirements

**Usuários e autenticação**
- **FR-001**: O sistema DEVE manter usuários com e-mail (único), nome e senha.
- **FR-002**: O sistema DEVE permitir login via OAuth, criando o usuário automaticamente no primeiro acesso.
- **FR-003**: O sistema NUNCA DEVE devolver a senha em respostas e DEVE armazená-la apenas como hash (quando existir).
- **FR-004**: O sistema DEVE exigir autenticação em todas as operações de clientes e pedidos.
- **FR-005**: O sistema DEVE permitir ao usuário consultar o próprio perfil.

**Clientes**
- **FR-006**: O sistema DEVE permitir cadastrar cliente com nome (obrigatório) e CPF (obrigatório e válido).
- **FR-007**: O sistema DEVE impedir dois clientes com o mesmo CPF para o mesmo usuário.
- **FR-008**: O sistema DEVE permitir listar clientes com paginação e busca por nome.
- **FR-009**: O sistema DEVE permitir consultar o detalhe de um cliente com resumo de compras (qtd. de compras, total líquido, última compra, dias desde a última compra).
- **FR-010**: O sistema DEVE permitir atualizar o nome do cliente.
- **FR-011**: O CPF DEVE ser tratado como dado pessoal: guardado de forma protegida (hash para busca/junção e valor cifrado) e exibido mascarado nas listagens (ex.: `***.456.789-**`).

**Pedidos**
- **FR-012**: O sistema DEVE permitir cadastrar pedido com: cliente, número, data, tipo (`sale`, `return`, `cancellation`), canal, origem (`cota` ou `omni`), status, total, data da venda original e data de cancelamento (opcionais), identificador externo (opcional).
- **FR-013**: O sistema DEVE exigir total positivo para venda e negativo para devolução e cancelamento.
- **FR-014**: O sistema DEVE impedir dois pedidos com o mesmo número, data e tipo para o mesmo usuário.
- **FR-015**: O sistema DEVE permitir cadastrar os itens do pedido: nome do produto, referência SKU, tamanho, preço e URL da imagem (referência), numerados em sequência dentro do pedido.
- **FR-016**: O sistema DEVE exigir ao menos um item por pedido.
- **FR-017**: O sistema DEVE permitir listar pedidos com paginação, filtros (período, tipo, canal, cliente, texto) e ordenação por data decrescente.
- **FR-018**: O sistema DEVE permitir consultar o detalhe de um pedido, com cliente e itens.
- **FR-019**: O sistema DEVE permitir atualizar status e total de um pedido.
- **FR-020**: O sistema DEVE registrar a data/hora de criação e de última alteração de clientes e pedidos.

**Isolamento**
- **FR-021**: O sistema DEVE restringir toda leitura e escrita de clientes e pedidos ao usuário dono.
- **FR-022**: O sistema DEVE responder "não encontrado" ao acessar recurso de outro usuário.
- **FR-023**: O sistema DEVE recusar vincular pedido a cliente de outro usuário.

**Gerais**
- **FR-024**: Erros de validação DEVEM retornar mensagens claras em português, indicando o campo inválido.
- **FR-025**: A API DEVE expor documentação interativa (OpenAPI/Swagger).

### Key Entities

- **Usuário**: pessoa que acessa a API. Atributos: e-mail (único), nome, senha (hash, opcional por causa do OAuth), provedor OAuth e identificador no provedor.
- **Cliente**: comprador atendido pelo usuário. Atributos: nome, CPF (protegido), usuário dono. Possui muitos pedidos.
- **Pedido**: venda, devolução ou cancelamento. Atributos: número, data, tipo, canal, origem, status, total, datas de venda original e cancelamento, identificador externo. Pertence a um cliente e a um usuário; possui muitos itens.
- **Item do pedido**: linha do pedido. Atributos: número da linha, nome do produto, SKU, tamanho, preço, URL da imagem.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Uma vendedora conclui o login e cadastra seu primeiro cliente e seu primeiro pedido em menos de 5 minutos, usando só a documentação da API.
- **SC-002**: Em testes com 2 ou mais usuários, 100% das tentativas de acessar dados de outro usuário são bloqueadas.
- **SC-003**: Listagens de clientes e pedidos respondem em até 1 segundo para até 10.000 pedidos por usuário.
- **SC-004**: 100% das operações de clientes e pedidos sem credencial válida são recusadas.
- **SC-005**: Nenhuma resposta da API expõe senha, e o CPF completo não aparece em listagens.
- **SC-006**: Os exemplos do doc 01 (pedido de venda, cancelamento, devolução com total negativo) são cadastrados sem perda de informação.

## Assumptions

- **Stack (decisão do solicitante)**: .NET e PostgreSQL. A constituição fixa .NET 8, EF Core 9 e Swashbuckle, com convenções de banco em snake_case, PK `{entidade}_id` bigint, e sem delete em cascata. Isso será detalhado em `/speckit.plan`.
- **Divergência com o doc 02**: o modelo do doc 02 usa `SERIAL`, `ON DELETE CASCADE` e `salesperson`. A constituição prevalece (bigint identity, `ClientSetNull`), e o conceito de `salesperson` é substituído pelo **usuário**.
- **Fora do escopo desta entrega**: front-end, bot Playwright (doc 03), MCP (doc 04), tabelas da fase 2 (contatos, perfil, interações, tags), `sales_period`, `period_kpi`, `raw_snapshot`, `product_image`, upload de imagens e dashboards de metas.
- **Esquema de autenticação**: a constituição deixa `TODO(AUTH_SCHEME)` em aberto. Esta feature o resolve com **OAuth 2.0/OpenID Connect via provedor externo** (assumido Google), e a constituição deve ser emendada depois.
- **Senha**: como o login é via OAuth, a senha é opcional e só existe se houver cadastro local. Login por senha local não faz parte desta entrega.
- **Cliente por usuário**: o cliente pertence a um único usuário (não há cliente compartilhado entre vendedoras).
- **Sem exclusão**: clientes e pedidos não são excluídos nesta entrega, só criados, consultados e atualizados.
- **Valores monetários**: em reais, com 2 casas decimais.
- **Dados do +Vendas** (telefone, e-mail, endereço etc. do cliente) não existem na origem e ficam para a fase 2.
