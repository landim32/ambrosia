# Feature Specification: Credenciais do +Vendas guardadas, importação pelo plugin e "Atualizar base"

**Feature Branch**: `008-import-credentials`
**Created**: 2026-10-28
**Status**: Draft
**Input**: User description: "Melhoria na importação: deve armazenar o usuário e senha, eles devem ficar criptografados na base. O usuário e senha saem da importação e vão para uma tela de configuração. A senha não é exibida na tela, só pode ser alterada. A importação deve ficar disponível no MCP, se deve passar a data inicial e final. O período máximo de importação deve ser de 3 meses. Deve ter uma opção de importação chamada "Atualizar base" que vai colocar a data inicial como a data do último pedido e a data final como a data atual."

## Contexto

Hoje (feature 006) a vendedora digita login e senha do +Vendas a cada importação, e o Ambrosio nunca guarda essas credenciais. No plugin do ChatGPT (feature 007), a importação só devolve um link para a tela, porque a senha não pode passar pela conversa. Esta melhoria muda as duas coisas: as credenciais passam a ficar guardadas, protegidas, numa tela de configuração, e com isso a importação pode ser iniciada direto pela tela ou pela conversa, só com o período — a senha continua sem nunca passar pela conversa.

## Clarifications

### Session 2026-10-28

- Q: Quando o intervalo do "Atualizar base" passa de 3 meses, o que fazer? → A: Recusar e pedir importações manuais em partes de até 3 meses (sem criar execução).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Guardar as credenciais do +Vendas numa tela de configuração (Priority: P1)

A vendedora abre a tela de configuração do +Vendas, informa o login e a senha uma vez e salva. Depois disso, ela vê o login salvo, mas a senha aparece só como "senha cadastrada", sem nunca ser exibida. Para trocar a senha, digita uma nova; para trocar de conta, muda o login e a senha; e pode remover as credenciais quando quiser.

**Why this priority**: Sem credenciais guardadas, nenhuma das outras histórias funciona (importar sem digitar a senha, importar pelo plugin, "Atualizar base").

**Independent Test**: Salvar login e senha na tela de configuração, sair e voltar: o login aparece, a senha não aparece em lugar nenhum (tela, resposta do servidor, log), e a base guarda a senha de forma ilegível.

**Acceptance Scenarios**:

1. **Given** a vendedora sem credenciais cadastradas, **When** informa login e senha e salva, **Then** a tela confirma o cadastro, mostra o login e indica "senha cadastrada" sem exibir a senha.
2. **Given** credenciais já cadastradas, **When** a vendedora abre a tela, **Then** o campo de senha vem vazio, com a indicação de que existe uma senha cadastrada, e o login aparece preenchido.
3. **Given** credenciais cadastradas, **When** a vendedora salva só um novo login sem informar senha, **Then** o sistema recusa e pede a senha (trocar de conta exige a senha da nova conta).
4. **Given** credenciais cadastradas, **When** a vendedora informa uma nova senha e salva, **Then** a senha anterior é substituída e continua sem ser exibida.
5. **Given** credenciais cadastradas, **When** a vendedora remove as credenciais, **Then** o login e a senha deixam de existir na base e as importações passam a pedir o cadastro.
6. **Given** qualquer situação, **When** alguém consulta os dados da configuração por qualquer canal (tela, API, plugin), **Then** a senha nunca é devolvida, nem mascarada.

---

### User Story 2 - Importar só com o período, usando as credenciais guardadas (Priority: P1)

Na tela de importação, a vendedora escolhe só as datas (e se é simulação) e inicia. O Ambrosio usa as credenciais guardadas. O período máximo passa a ser de 3 meses.

**Why this priority**: É a mudança de rotina mais frequente: importar sem redigitar a senha toda vez.

**Independent Test**: Com credenciais cadastradas, abrir a importação, escolher um período de até 3 meses e iniciar: a execução roda como antes, sem campo de login nem senha na tela.

**Acceptance Scenarios**:

1. **Given** credenciais cadastradas, **When** a vendedora abre a tela de importação, **Then** não há campos de login nem senha; há data inicial, data final, simulação e o botão de iniciar.
2. **Given** credenciais não cadastradas, **When** a vendedora abre a tela de importação, **Then** a tela avisa que é preciso cadastrar as credenciais e leva à tela de configuração; não é possível iniciar.
3. **Given** um período maior que 3 meses, **When** a vendedora tenta iniciar, **Then** o sistema recusa com a mensagem "o período não pode passar de 3 meses", na tela e no servidor.
4. **Given** credenciais cadastradas mas recusadas pelo +Vendas (senha trocada lá), **When** a importação roda, **Then** ela termina como "credenciais recusadas pelo +Vendas", sem gravar nada, e a tela de configuração passa a indicar que as credenciais precisam ser atualizadas.

---

### User Story 3 - "Atualizar base" (Priority: P2)

A vendedora clica em "Atualizar base" e a importação é preparada sozinha: data inicial = data do último pedido importado do +Vendas, data final = hoje. Ela confirma e a importação roda.

**Why this priority**: É o atalho do uso diário (trazer as vendas novas desde a última importação), mas depende das histórias 1 e 2.

**Independent Test**: Com pedidos já importados e credenciais cadastradas, acionar "Atualizar base": o período sugerido vai da data do último pedido importado até hoje, e a importação roda com esse período.

**Acceptance Scenarios**:

1. **Given** o último pedido importado do +Vendas em 20/10 e hoje 28/10, **When** a vendedora aciona "Atualizar base", **Then** a importação é preparada de 20/10 a 28/10 (o dia do último pedido é reimportado para pegar pedidos do mesmo dia que ficaram de fora).
2. **Given** nenhum pedido importado do +Vendas ainda, **When** a vendedora aciona "Atualizar base", **Then** o sistema avisa que não há base a atualizar e pede uma importação com datas escolhidas.
3. **Given** o último pedido importado há mais de 3 meses, **When** a vendedora aciona "Atualizar base", **Then** o sistema recusa com a mensagem de que o intervalo desde a última importação passa de 3 meses e orienta a importar em partes de até 3 meses, escolhendo as datas; nenhuma execução é criada.

---

### User Story 4 - Importar pelo plugin do ChatGPT (Priority: P2)

Na conversa, a vendedora pede "importe minhas vendas de 01/10 a 25/10" ou "atualize minha base". O plugin mostra a prévia (período, simulação), pede confirmação e inicia a importação com as credenciais guardadas. Depois ela pergunta "como está a importação?" e acompanha pela conversa.

**Why this priority**: Fecha a paridade do plugin com a tela, mas depende das credenciais guardadas.

**Independent Test**: Pela conversa, pedir a importação de um período: aparece a prévia; ao confirmar, a execução é criada e aparece também na tela de importação, sem que login ou senha tenham passado pela conversa.

**Acceptance Scenarios**:

1. **Given** credenciais cadastradas, **When** a vendedora pede pelo plugin uma importação com data inicial e final, **Then** o plugin mostra a prévia e, depois da confirmação, inicia a importação e devolve o número da execução.
2. **Given** credenciais cadastradas, **When** a vendedora pede pelo plugin para "atualizar a base", **Then** o plugin prepara o período do último pedido importado até hoje, mostra a prévia e inicia depois da confirmação.
3. **Given** credenciais não cadastradas, **When** a vendedora pede uma importação pelo plugin, **Then** o plugin responde que é preciso cadastrar as credenciais na tela de configuração do Ambrosio e devolve o link dessa tela.
4. **Given** qualquer pedido pela conversa, **When** a vendedora tenta passar login ou senha do +Vendas para o plugin, **Then** o plugin recusa e orienta a cadastrar na tela de configuração; nada é gravado e nada aparece em log ou auditoria.
5. **Given** um período maior que 3 meses pedido pela conversa, **When** o plugin prepara a importação, **Then** recusa com a mesma mensagem da tela.

---

### Edge Cases

- **Credenciais recusadas várias vezes**: a regra atual de bloqueio após recusas seguidas (feature 006) continua valendo; com credenciais guardadas, a importação recusada também marca as credenciais como "precisam ser atualizadas" até a vendedora salvar uma nova senha.
- **Troca de conta do +Vendas**: a regra atual de vínculo da conta (feature 006) continua valendo; salvar o login de outra conta não muda o vínculo, e a importação com conta diferente da vinculada continua recusada como hoje.
- **Importação em andamento e troca de senha**: a execução em curso continua com as credenciais que já carregou; a nova senha vale a partir da próxima importação.
- **Remoção das credenciais com importação em andamento**: a execução em curso continua até o fim; novas importações ficam indisponíveis.
- **Período exatamente de 3 meses**: aceito (ex.: 26/07 a 25/10). Um dia a mais é recusado.
- **Data do último pedido igual a hoje**: "Atualizar base" importa só o dia de hoje.
- **Pedido manual mais recente que o último importado**: não afeta o "Atualizar base", que considera só os pedidos importados do +Vendas.
- **Conta do Ambrosio excluída**: as credenciais guardadas são apagadas junto.

## Requirements *(mandatory)*

### Functional Requirements

**Credenciais e tela de configuração**

- **FR-001**: O sistema DEVE oferecer uma tela de configuração do +Vendas, acessível só à própria vendedora autenticada, para cadastrar, alterar e remover login e senha do +Vendas.
- **FR-002**: O sistema DEVE guardar login e senha do +Vendas de forma criptografada na base, legível apenas pelo próprio sistema no momento de executar uma importação, com a chave de criptografia guardada fora da base.
- **FR-003**: A senha NUNCA DEVE ser exibida nem devolvida — nem completa, nem mascarada — em tela, resposta de qualquer endpoint, plugin, log, relatório ou auditoria. A tela mostra apenas se existe senha cadastrada e quando ela foi alterada pela última vez.
- **FR-004**: O login cadastrado PODE ser exibido na tela de configuração da própria vendedora.
- **FR-005**: Alterar a senha DEVE substituir a anterior; trocar o login DEVE exigir informar também a senha.
- **FR-006**: Remover as credenciais DEVE apagá-las da base; a partir daí, importações (tela, plugin e "Atualizar base") ficam indisponíveis até um novo cadastro.
- **FR-007**: Quando o +Vendas recusar as credenciais guardadas, a execução DEVE terminar como hoje ("credenciais recusadas pelo +Vendas", sem gravar nada) e a tela de configuração DEVE indicar que as credenciais precisam ser atualizadas, até a vendedora salvar uma nova senha.
- **FR-008**: As credenciais de uma vendedora NUNCA DEVEM ser usadas, lidas ou alteradas por outra.

**Importação**

- **FR-009**: A tela de importação NÃO DEVE mais pedir login nem senha; ela usa as credenciais guardadas. Sem credenciais cadastradas, a tela DEVE indicar o cadastro e levar à tela de configuração, sem permitir iniciar.
- **FR-010**: O período máximo de uma importação DEVE ser de 3 meses corridos: a data final pode ser no máximo o dia anterior à mesma data do terceiro mês seguinte à data inicial (ex.: início 26/07, fim até 25/10). A regra vale na tela, no servidor e no plugin, com a mesma mensagem, e substitui o limite atual de 366 dias.
- **FR-011**: As demais regras de período continuam: data inicial ≤ data final, data final não futura, datas obrigatórias.
- **FR-012**: As regras atuais de execução (blocos, simulação, uma importação por vendedora por vez, fila, cancelamento, bloqueio após recusas, vínculo da conta, relatório) DEVEM continuar valendo sem mudança.

**"Atualizar base"**

- **FR-013**: O sistema DEVE oferecer a opção "Atualizar base", na tela de importação e no plugin, que prepara a importação com data inicial = data do último pedido importado do +Vendas pela vendedora (inclusive) e data final = hoje no fuso de São Paulo.
- **FR-014**: Sem nenhum pedido importado do +Vendas, "Atualizar base" DEVE ficar indisponível, com a orientação de fazer uma importação escolhendo as datas.
- **FR-015**: Quando o intervalo do "Atualizar base" (do último pedido importado até hoje) passar de 3 meses, o sistema DEVE recusar sem criar execução, na tela e no plugin, com a orientação de importar em partes de até 3 meses escolhendo as datas.

**Plugin do ChatGPT**

- **FR-016**: O plugin DEVE permitir iniciar uma importação informando data inicial e data final (e, opcionalmente, simulação), com prévia e confirmação antes de iniciar, usando as credenciais guardadas, e devolver o número da execução.
- **FR-017**: O plugin DEVE permitir acionar o "Atualizar base", com prévia do período calculado e confirmação antes de iniciar.
- **FR-018**: O plugin NÃO DEVE aceitar login nem senha do +Vendas em nenhuma ação; sem credenciais cadastradas, DEVE responder com a orientação e o link da tela de configuração.
- **FR-019**: O plugin NÃO DEVE oferecer ação de consultar, cadastrar ou alterar as credenciais do +Vendas; isso só é feito na tela de configuração.
- **FR-020**: Toda importação iniciada pelo plugin DEVE ser registrada na auditoria do plugin (período, simulação, resultado do início), sem credenciais.

### Key Entities

- **Credenciais do +Vendas**: uma por vendedora. Login (criptografado), senha (criptografada), data de cadastro, data da última alteração da senha e situação ("válidas" ou "precisam ser atualizadas" após recusa do +Vendas). Pertencem a uma única vendedora e são apagadas junto com a conta.
- **Execução de importação** (existente, feature 006): passa a registrar se foi iniciada pela tela, pelo plugin ou pelo "Atualizar base". Nenhuma credencial é gravada nela.
- **Conta do +Vendas vinculada** (existente, feature 006): sem mudança; continua sendo a referência para recusar importações de outra conta.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Com as credenciais cadastradas, a vendedora inicia uma importação pela tela em até 3 interações (escolher datas ou "Atualizar base", confirmar), sem digitar login nem senha.
- **SC-002**: A vendedora inicia uma importação pela conversa do ChatGPT em uma pergunta e uma confirmação, e a execução aparece na tela de importação em até 5 segundos.
- **SC-003**: Em 100% das respostas de tela, servidor, plugin, logs, relatórios e auditoria inspecionados, a senha do +Vendas não aparece de forma nenhuma.
- **SC-004**: Quem tiver acesso de leitura à base de dados não consegue obter a senha nem o login do +Vendas sem a chave guardada fora dela.
- **SC-005**: 100% das tentativas de importação com período maior que 3 meses são recusadas, com a mesma mensagem na tela, no servidor e no plugin.
- **SC-006**: "Atualizar base" sugere corretamente o período (último pedido importado até hoje) em 100% dos casos com pedidos importados.

## Assumptions

- "Último pedido" do "Atualizar base" é o pedido mais recente **importado do +Vendas** pela vendedora (a mesma referência da sugestão de data inicial da feature 006); pedidos cadastrados à mão não contam, porque a importação busca o que está no +Vendas.
- O dia do último pedido é reimportado (data inicial inclusive): a importação é idempotente (feature 006), então pedidos já gravados ficam como "sem mudança" e os do mesmo dia que ainda não estavam na base entram.
- As credenciais não são testadas no +Vendas no momento de salvar: a validação acontece na primeira importação, como hoje (abrir o +Vendas só para testar custaria uma sessão de navegador e contaria nas recusas seguidas).
- O login do +Vendas é exibido na tela de configuração da própria vendedora (é ela quem o informou); a criptografia na base protege login e senha contra quem lê a base.
- A chave de criptografia segue o mesmo modelo dos outros segredos do sistema (configurada por ambiente, fora do repositório e da base), como já acontece com a proteção do CPF.
- Importação automática agendada (sem a vendedora pedir) fica fora do escopo; guardar as credenciais a torna possível numa feature futura.
- O modo "simulação" continua disponível na tela, no plugin e no "Atualizar base".
- Esta feature revê uma decisão de segurança da feature 006 ("nenhuma credencial do +Vendas é gravada") e do plugin (R9 da feature 007, link para a tela): a decisão passa a ser guardar as credenciais criptografadas, sem nunca exibi-las nem aceitá-las pela conversa.

## Dependencies

- Feature 006 (importação do +Vendas): coletor, execução em blocos, relatório, bloqueio após recusas e vínculo da conta.
- Feature 007 (plugin do ChatGPT): tools de importação, prévia e confirmação, auditoria.
