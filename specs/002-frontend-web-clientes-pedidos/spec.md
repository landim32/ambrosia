# Feature Specification: Frontend web de consulta de clientes e pedidos

**Feature Branch**: `002-frontend-web-clientes-pedidos`

**Created**: 2026-10-26

**Status**: Draft

**Input**: User description: "Como vendedora, quero abrir no navegador (inclusive no celular) o perfil dos meus clientes e os meus pedidos, para consultar rapidamente quem comprou o quê, quanto e quando, sem precisar do Swagger nem do +Vendas. Frontend web, só de leitura, que mostra o perfil do cliente e os pedidos usando a API da HU 001." (HU 002 do backlog, `docs/historias/002-frontend-web-perfil-cliente-pedidos.md`)

## Contexto

A HU 001 entregou somente a API do Ambrosia: hoje a vendedora só consegue consultar seus clientes e pedidos pelo Swagger ou por linha de comando. Esta feature cria o **primeiro frontend web**, **somente para consulta** (nenhuma tela de cadastro, edição ou exclusão). Os dados continuam entrando pela API, pelo bot de migração (HU 009) ou pelo MCP (HU 006).

Assim como o +Vendas (doc 01), o uso principal é no **celular, dentro da loja**: a interface é pensada primeiro para telas pequenas, com listas em cards, e também funciona no desktop.

## Clarifications

### Session 2026-10-26

- Q: Como o CPF deve aparecer no perfil do cliente? → A: Mascarado por padrão, com botão "mostrar" que revela o CPF completo sob demanda.
- Q: A lista de pedidos deve ter seletor de "mês de cota" (26 a 25) além do período livre? → A: Não; só período livre nesta entrega. O mês de cota fica para uma HU futura com dashboards.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Entrar e manter a sessão (Priority: P1)

A vendedora abre o endereço do Ambrosia no navegador, entra com sua conta Google (a mesma usada na API) e passa a ver apenas os seus dados. Enquanto a sessão for válida, ela não precisa entrar de novo; quando a sessão expira, é levada de volta à tela de entrada com um aviso.

**Why this priority**: sem sessão autenticada nenhuma outra tela pode mostrar dados; é a base de todas as demais histórias e o que garante o isolamento entre vendedoras.

**Independent Test**: abrir o site sem sessão, ser direcionada à tela de entrada, concluir o login com Google, ver o próprio nome e e-mail no cabeçalho e, ao sair, voltar à tela de entrada.

**Acceptance Scenarios**:

1. **Given** uma vendedora sem sessão ativa, **When** tenta abrir qualquer tela de clientes ou pedidos, **Then** é direcionada à tela de entrada e, após entrar, retorna à tela que tentou abrir.
2. **Given** a tela de entrada, **When** conclui o login com Google, **Then** a sessão é iniciada, o cabeçalho mostra seu nome e e-mail e ela vê a lista de clientes.
3. **Given** uma sessão ativa, **When** fecha o navegador e o reabre antes do prazo de expiração, **Then** continua conectada sem precisar entrar de novo.
4. **Given** uma sessão cujo prazo expirou, **When** abre qualquer tela ou faz qualquer consulta, **Then** a sessão é encerrada localmente e ela volta à tela de entrada com o aviso "Sua sessão expirou".
5. **Given** uma sessão ativa, **When** a API recusa a credencial (sessão inválida), **Then** a sessão é encerrada e ela volta à tela de entrada com o mesmo aviso.
6. **Given** uma sessão ativa, **When** aciona "Sair", **Then** a sessão é encerrada e nenhuma informação dela permanece acessível no navegador.
7. **Given** um ambiente de desenvolvimento ou homologação, **When** abre a tela de entrada, **Then** vê também um formulário de entrada de desenvolvimento (e-mail e nome); **Given** o site publicado em produção, **Then** esse formulário não existe nem é incluído no pacote publicado.

---

### User Story 2 - Encontrar um cliente e ver seu perfil (Priority: P1)

Na loja, a vendedora procura uma cliente pelo nome, abre o perfil e vê rapidamente quem ela é, quanto já comprou, quando foi a última compra e a lista de pedidos dela.

**Why this priority**: é o coração da história ("quem comprou o quê, quanto e quando") e o caso de uso mais frequente no atendimento.

**Independent Test**: com uma base que tenha mais de 20 clientes, buscar por um trecho do nome, abrir o perfil e conferir que o resumo de compras e os pedidos batem com a API.

**Acceptance Scenarios**:

1. **Given** a vendedora conectada, **When** abre a lista de clientes, **Then** vê cards com nome e CPF mascarado, 20 por página, com controles para avançar e voltar páginas e o total de clientes.
2. **Given** a lista de clientes, **When** digita um trecho do nome, **Then** a lista é filtrada automaticamente após uma breve pausa na digitação (sem precisar apertar um botão), e a busca ignora maiúsculas/minúsculas.
3. **Given** uma busca sem resultado, **When** a lista carrega, **Then** aparece uma mensagem de "nenhum cliente encontrado" e uma forma de limpar a busca.
4. **Given** a lista de clientes, **When** toca em um card, **Then** abre o perfil daquele cliente.
5. **Given** o perfil de um cliente, **When** a tela carrega, **Then** mostra o nome, o CPF mascarado com um botão "mostrar", e o resumo: quantidade de compras, total líquido em reais, data da última compra e dias desde a última compra.
6. **Given** o perfil com o CPF mascarado, **When** toca em "mostrar", **Then** o CPF completo formatado aparece e o botão passa a "ocultar"; ao sair do perfil ou tocar em "ocultar", volta a mascarado.
7. **Given** um cliente sem foto, **When** o perfil abre, **Then** aparece um avatar com as iniciais do nome; **Given** um cliente com foto (vinda da HU 010), **Then** aparece a foto.
8. **Given** o perfil de um cliente, **When** a tela carrega, **Then** lista os pedidos daquele cliente (mais recentes primeiro), cada um levando ao detalhe do pedido.
9. **Given** um cliente que nunca comprou, **When** o perfil abre, **Then** o resumo mostra zero compras, total R$ 0,00 e "sem compras" no lugar da última compra, e a lista de pedidos mostra um estado vazio.
10. **Given** um identificador de cliente inexistente ou pertencente a outra vendedora, **When** tenta abrir o perfil, **Then** vê a tela "Cliente não encontrado", sem diferença entre os dois casos.

---

### User Story 3 - Consultar os pedidos com filtros (Priority: P2)

A vendedora abre a lista de todos os seus pedidos, filtra por período, tipo (venda, devolução, cancelamento), canal ou texto, e identifica de relance quais são devoluções e cancelamentos.

**Why this priority**: complementa a visão por cliente com a visão cronológica das vendas; é o que substitui a consulta de pedidos do +Vendas, mas depende menos do atendimento imediato na loja.

**Independent Test**: com pedidos de venda, devolução e cancelamento cadastrados, aplicar cada filtro, recarregar a página e confirmar que o filtro se mantém e que os negativos têm destaque.

**Acceptance Scenarios**:

1. **Given** a vendedora conectada, **When** abre a lista de pedidos, **Then** vê cards com nome do cliente, número, data, canal, tipo, etiqueta de origem (quando houver) e total, ordenados da data mais recente para a mais antiga, 20 por página.
2. **Given** a lista de pedidos, **When** filtra por período (data inicial e final), tipo, canal ou texto (número do pedido ou nome do cliente), **Then** a lista mostra apenas os pedidos correspondentes e o total de resultados.
3. **Given** filtros aplicados, **When** recarrega a página ou compartilha o endereço, **Then** os mesmos filtros continuam aplicados.
4. **Given** pedidos de devolução ou cancelamento, **When** aparecem na lista, **Then** o total é exibido negativo (ex.: `-R$ 1.234,56`) e com destaque visual distinto do de venda.
5. **Given** filtros que não correspondem a nenhum pedido, **When** a lista carrega, **Then** aparece "nenhum pedido encontrado" e uma forma de limpar os filtros.
6. **Given** a lista de pedidos, **When** toca em um card, **Then** abre o detalhe daquele pedido.

---

### User Story 4 - Ver o detalhe de um pedido (Priority: P2)

A vendedora abre um pedido e vê o cliente, os dados do pedido e cada item comprado, com foto, nome, referência, tamanho e preço.

**Why this priority**: responde ao "o quê" da história no nível mais granular; depende das listas (US2/US3) para ser alcançado.

**Independent Test**: abrir um pedido de venda com vários itens e um de cancelamento, e conferir os campos exibidos contra a API.

**Acceptance Scenarios**:

1. **Given** um pedido existente, **When** abre o detalhe, **Then** vê o nome do cliente (com link para o perfil) e o CPF mascarado, número, data, tipo, canal, origem, status e total.
2. **Given** um pedido de cancelamento ou devolução, **When** abre o detalhe, **Then** vê também a data da venda original e a data do cancelamento, quando informadas, e o total negativo com destaque.
3. **Given** um pedido com itens, **When** abre o detalhe, **Then** vê cada item na ordem de linha, com imagem, nome do produto, referência (SKU), tamanho e preço em reais.
4. **Given** um item sem imagem ou cuja imagem não carrega, **When** o detalhe abre, **Then** um espaço reservado (placeholder) ocupa o lugar da imagem sem quebrar o layout.
5. **Given** um identificador de pedido inexistente ou de outra vendedora, **When** tenta abrir, **Then** vê "Pedido não encontrado".

---

### User Story 5 - Usar no celular com conforto (Priority: P2)

A vendedora usa o Ambrosia no celular na loja, com rede instável, e precisa que as telas caibam, respondam ao toque e avisem quando algo falhar.

**Why this priority**: é a condição de uso real do produto (doc 01: o +Vendas é mobile-first); sem isso as histórias anteriores não entregam valor na loja.

**Independent Test**: percorrer todas as telas em uma janela de 360 px de largura e com a rede desligada, verificando layout, foco visível e mensagens.

**Acceptance Scenarios**:

1. **Given** uma tela de 360 px de largura, **When** abre qualquer tela, **Then** nada fica cortado nem exige rolagem horizontal, e os alvos de toque são confortáveis.
2. **Given** qualquer consulta em andamento, **When** os dados ainda não chegaram, **Then** a tela mostra um indicador de carregamento no lugar do conteúdo.
3. **Given** uma falha de rede ou um erro da API, **When** a consulta falha, **Then** aparece um aviso curto com a mensagem retornada pela API (ou "Não foi possível conectar") e uma opção de tentar de novo.
4. **Given** navegação por teclado ou leitor de tela, **When** percorre as telas, **Then** todos os controles têm rótulo, foco visível e contraste adequado.

---

### Edge Cases

- **Sessão expira no meio da navegação**: ao abrir a próxima tela ou consulta, a vendedora é levada à entrada com o aviso; ao reentrar, volta à tela em que estava.
- **Datas sem hora** (data do pedido, venda original, cancelamento): devem aparecer no mesmo dia em qualquer fuso horário do navegador, sem deslocamento de um dia.
- **Período dos filtros**: as datas inicial e final escolhidas pela vendedora são enviadas explicitamente à API, no fuso de Brasília (a API trata "hoje" em UTC).
- **Página além do fim** (ex.: endereço com página 99 em uma lista de 2 páginas): mostra o estado vazio e permite voltar à primeira página.
- **Filtro inválido no endereço** (tipo desconhecido, data mal formada): o filtro inválido é ignorado e a lista carrega sem ele.
- **Nome muito longo** no card ou no cabeçalho: truncado com reticências, sem quebrar o layout.
- **Imagem do item vinda do domínio do +Vendas** que expira ou é bloqueada: placeholder, sem erro visível e sem vazar a origem da vendedora ao servidor da imagem.
- **CPF revelado no perfil**: o estado "mostrar" não é lembrado; ao reabrir o perfil o CPF volta a mascarado. Nunca aparece completo em listagens nem no detalhe do pedido.
- **Acesso direto a um endereço de tela interna sem sessão**: redireciona para a entrada e, depois, para o endereço original.
- **Canal desconhecido** (texto livre): exibido como veio, sem tradução.

## Requirements *(mandatory)*

### Functional Requirements

**Sessão e acesso**

- **FR-001**: O sistema DEVE exigir sessão autenticada para todas as telas, exceto a tela de entrada.
- **FR-002**: O sistema DEVE permitir entrar com a conta Google e, em ambientes que não sejam produção, também pelo formulário de entrada de desenvolvimento (e-mail e nome); em produção esse formulário NÃO DEVE existir nem ser incluído no pacote publicado.
- **FR-003**: O sistema DEVE manter a sessão entre aberturas do navegador até o prazo de expiração informado pela API.
- **FR-004**: O sistema DEVE encerrar a sessão e voltar à tela de entrada com o aviso "Sua sessão expirou" quando o prazo expirar localmente ou quando a API recusar a credencial.
- **FR-005**: O sistema DEVE oferecer "Sair", encerrando a sessão e removendo todo dado dela guardado no navegador.
- **FR-006**: O sistema DEVE mostrar nome e e-mail da vendedora conectada no cabeçalho de todas as telas internas.
- **FR-007**: Após entrar, o sistema DEVE levar a vendedora à tela que ela tentou abrir originalmente (ou à lista de clientes, se não houver).

**Clientes**

- **FR-008**: O sistema DEVE listar os clientes da vendedora em cards com nome e CPF mascarado, paginados (20 por página) e com total de registros.
- **FR-009**: O sistema DEVE permitir buscar clientes por trecho do nome, aplicando a busca automaticamente após breve pausa na digitação.
- **FR-010**: O sistema DEVE exibir o perfil do cliente com nome, CPF (ver FR-012), avatar (iniciais ou foto, quando houver) e resumo de compras: quantidade de compras, total líquido, data da última compra e dias desde a última compra.
- **FR-011**: O perfil DEVE listar os pedidos do cliente, mais recentes primeiro, com acesso ao detalhe de cada um.
- **FR-012**: No perfil, o CPF DEVE aparecer mascarado (`***.456.789-**`) por padrão, com um botão "mostrar" que revela o CPF completo formatado e passa a "ocultar"; o estado revelado NÃO DEVE ser lembrado entre aberturas do perfil.

**Pedidos**

- **FR-013**: O sistema DEVE listar os pedidos da vendedora em cards com nome do cliente, número, data, canal, tipo, etiqueta de origem e total, ordenados por data decrescente e paginados (20 por página).
- **FR-014**: O sistema DEVE permitir filtrar pedidos por período livre (data inicial e final), tipo, canal e texto (número do pedido ou nome do cliente), e DEVE permitir limpar os filtros.
- **FR-015**: Os filtros DEVEM ficar refletidos no endereço da página, de modo que recarregar ou compartilhar o endereço mantenha os mesmos filtros.
- **FR-016**: O período dos pedidos é filtrado apenas por datas livres nesta entrega; o sistema NÃO DEVE oferecer seletor de "mês de cota" (26 a 25), que fica para uma história futura com dashboards.
- **FR-017**: O sistema DEVE exibir o detalhe do pedido com: cliente (nome com link para o perfil e CPF mascarado), número, data, tipo, canal, origem, status, total, data da venda original e data do cancelamento (quando houver) e os itens em ordem de linha, cada um com imagem, nome do produto, referência, tamanho e preço.
- **FR-018**: Pedidos de devolução e cancelamento DEVEM mostrar o total negativo e com destaque visual distinto do de venda, em listas e no detalhe.

**Exibição e formato**

- **FR-019**: Valores monetários DEVEM ser exibidos em reais no formato brasileiro (ex.: `R$ 1.234,56`; negativos como `-R$ 1.234,56`).
- **FR-020**: Datas sem hora DEVEM ser exibidas como `dd/mm/aaaa` e representar o mesmo dia em qualquer fuso horário do navegador.
- **FR-021**: Tipos de pedido DEVEM aparecer em português: venda, devolução e cancelamento. Origem (`cota`, `omni`) aparece como etiqueta; canal é exibido como veio.
- **FR-022**: O CPF completo NÃO DEVE aparecer em listagens nem no detalhe do pedido; apenas o mascarado. O único lugar em que pode ser revelado é o perfil do cliente (FR-012).
- **FR-023**: Imagens de itens DEVEM carregar sob demanda, com espaço reservado em caso de ausência ou falha, e sem enviar a origem do Ambrosia ao servidor da imagem.
- **FR-024**: Toda a interface DEVE estar em português do Brasil.

**Estados, erros e acessibilidade**

- **FR-025**: Toda consulta DEVE ter estados de carregando, vazio ("nenhum cliente/pedido encontrado") e erro.
- **FR-026**: Erros da API DEVEM ser exibidos em um aviso curto com a mensagem retornada; falhas de rede DEVEM mostrar "Não foi possível conectar" com opção de tentar de novo.
- **FR-027**: Recursos inexistentes ou de outra vendedora DEVEM mostrar a tela "não encontrado", sem distinção entre os dois casos.
- **FR-028**: O layout DEVE funcionar a partir de 360 px de largura, sem rolagem horizontal.
- **FR-029**: Todos os controles DEVEM ter rótulo acessível, foco visível e contraste adequado (verificações básicas de acessibilidade).
- **FR-030**: Nenhuma tela DEVE ter ação de criar, editar ou excluir clientes ou pedidos.

### Key Entities

Esta feature não cria dados; apenas apresenta os da HU 001.

- **Vendedora (usuário)**: pessoa conectada. Exibidos: nome e e-mail. Nunca a senha.
- **Sessão**: credencial recebida da API após o login, com prazo de expiração; guardada no navegador até sair ou expirar.
- **Cliente**: comprador da vendedora. Exibidos: nome, CPF (mascarado; completo só no perfil, sob demanda), foto (opcional, HU 010) e resumo de compras (quantidade, total líquido, última compra, dias desde a última compra).
- **Pedido**: venda, devolução ou cancelamento de um cliente. Exibidos: número, data, tipo, canal, origem, status, total, datas de venda original e cancelamento, quantidade de itens.
- **Item do pedido**: linha do pedido. Exibidos: ordem, nome do produto, referência (SKU), tamanho, preço e imagem.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A partir da tela de entrada, uma vendedora encontra uma cliente pelo nome e abre o perfil com o resumo de compras em menos de 30 segundos no celular.
- **SC-002**: 100% das tentativas de abrir telas internas sem sessão válida terminam na tela de entrada; 100% das sessões expiradas ou recusadas levam à entrada com o aviso.
- **SC-003**: O CPF completo não aparece em nenhuma listagem nem no detalhe do pedido em 100% das telas verificadas, e no perfil só aparece após a ação "mostrar".
- **SC-004**: Pedidos de devolução e cancelamento são identificados corretamente por 100% das vendedoras em teste de uso, apenas pelo destaque visual e pelo sinal negativo.
- **SC-005**: Listas e detalhes aparecem em até 2 segundos em conexão móvel típica (4G), para bases de até 10.000 pedidos por vendedora.
- **SC-006**: Todas as telas passam nas verificações básicas de acessibilidade (contraste, foco, rótulos) e funcionam em largura de 360 px sem rolagem horizontal.
- **SC-007**: Datas de pedidos aparecem no dia correto em 100% dos casos, testadas em pelo menos 3 fusos horários distintos.
- **SC-008**: O pacote publicado em produção não contém o formulário de entrada de desenvolvimento.
- **SC-009**: A vendedora deixa de precisar do Swagger ou do +Vendas para consultar clientes e pedidos: todos os campos expostos pela API de consulta (HU 001) estão visíveis em alguma tela.

## Assumptions

- **Somente consulta**: nenhuma operação de escrita faz parte desta entrega. Cadastro e edição continuam pela API, bot (HU 009) ou MCP (HU 006).
- **Fonte de dados**: exclusivamente a API REST da HU 001, já implementada. O GraphQL (HU 005) não é pré-requisito.
- **Autenticação**: reaproveita o login da HU 001 (conta Google, com a entrada de desenvolvimento permitida apenas fora de produção, conforme a constituição 3.0.0). O prazo de sessão é o informado pela API (padrão 8 h).
- **Entrada de desenvolvimento**: visível em Development e Homolog (onde a API a aceita) e ausente do pacote de produção. Resolve a pergunta 2 do backlog com a regra já fixada na constituição.
- **Hospedagem**: frontend e API na mesma origem (`ambrosiaai.site` e `ambrosiaai.site/api`), conforme decidido em 25/10/2026; detalhes de infraestrutura ficam para o plano.
- **Chave de armazenamento da sessão**: segue a constituição vigente (pergunta 4 do backlog); eventual renomeação é emenda futura, fora desta feature.
- **Fotos de clientes**: a API da HU 001 ainda não devolve foto; o perfil mostra iniciais até a HU 010 existir, e deve estar pronto para exibir a foto quando o campo surgir.
- **Idioma**: somente português do Brasil nesta entrega, embora a base de internacionalização seja preparada.
- **Mês de cota (26 a 25)**: fora do escopo, por decisão do solicitante (pergunta 3 do backlog). Será tratado junto com os dashboards de cota.
- **Dashboards de cota, supercota, PA, VA e TMA**: fora do escopo (a API não tem os dados).
- **PWA, instalação e uso offline**: fora do escopo (HU 004).
- **Paginação**: 20 por página, igual ao padrão da API.
- **Dependência de backend para produção**: a API precisa aceitar ser servida na subpasta `/api` (ajuste da HU 001, fora deste frontend).
