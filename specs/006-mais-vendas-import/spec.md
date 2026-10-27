# Feature Specification: Importação do +Vendas pelo frontend (pedidos, clientes e fotos)

**Feature Branch**: `006-mais-vendas-import`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "docs/historias/004-bot-migracao-mais-vendas.md" (HU 004 — tela no frontend em que a vendedora informa o login e a senha do +Vendas e o período desejado, e o servidor importa os pedidos em blocos, de forma idempotente, cadastrando os clientes sob demanda e copiando as fotos dos produtos para o storage)

## Contexto

A vendedora registra as vendas no +Vendas (sistema da Somalabs) e hoje precisa redigitar tudo no Ambrosia. O +Vendas ainda altera vendas até o fim do primeiro dia útil depois do fechamento do mês de cota (dia 25), então qualquer importação precisa poder ser **repetida sobre o mesmo período sem duplicar nada**, atualizando só o que mudou.

Esta feature entrega uma **tela de importação** no Ambrosia: a vendedora informa o login e a senha do +Vendas e o período que quer trazer; o servidor coleta os pedidos do +Vendas **somente em leitura**, em **blocos de datas**, valida cada bloco contra os contadores e totais exibidos pela origem, grava de forma idempotente, cadastra os clientes quando aparecem no primeiro pedido e copia as fotos dos produtos para o storage de imagens já existente. As credenciais do +Vendas vivem só em memória durante a execução e **nunca** são guardadas.

A feature reaproveita o modelo e a proteção do CPF da feature 001, o frontend e a sessão da 002, a autenticação por e-mail e senha da 003 e o storage de imagens da 004 (`004-s3-image-storage`). A análise do +Vendas (doc 01), o modelo de dados (doc 02) e as regras de segurança da coleta (doc 03) continuam valendo; o que muda em relação à versão anterior da HU é que a importação deixa de ser um processo agendado com credenciais em arquivo e passa a ser **disparada pela própria vendedora**, a cada vez, com credenciais informadas na hora.

## Clarifications

### Session 2026-10-27

- Q: Quando uma execução termina como "parcial" em vez de "concluída" ou "falhou"? → A: Quando todos os blocos terminaram sem falha, mas houve pelo menos um pedido "com erro", um conflito com pedido manual ou uma imagem com erro; "falhou" fica reservado para bloco falho, credenciais recusadas, login não suportado ou interrupção.
- Q: O que acontece com a edição manual feita no Ambrosia de um pedido importado quando a origem muda e o período é reimportado? → A: A origem prevalece: os campos vindos do +Vendas são sobrescritos (o pedido conta como "atualizado", com a lista de campos alterados) e os campos que só existem no Ambrosia são preservados.
- Q: O Ambrosia deve limitar tentativas seguidas com credenciais recusadas pelo +Vendas? → A: Sim; após 3 recusas seguidas da mesma vendedora, novos inícios ficam bloqueados por 15 minutos com mensagem clara; a contagem zera após um login aceito.
- Q: O que fazer se a vendedora importar com uma conta do +Vendas diferente da usada antes? → A: Bloquear: na primeira importação com gravação, guarda-se só um hash com pepper do identificador da conta na origem; uma conta diferente depois faz a execução falhar antes de gravar, com mensagem clara.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Importar um período do +Vendas informando as credenciais na hora (Priority: P1)

A vendedora autenticada abre a tela de importação, encontra a data inicial sugerida (data do último pedido já importado do +Vendas) e a data final (hoje) preenchidas, informa o login e a senha do +Vendas e inicia a importação. O período é dividido em blocos; a tela mostra o bloco em andamento, quantos já terminaram e as contagens parciais. Ao final, um relatório mostra o que foi inserido, atualizado e mantido, por bloco, canal e tipo de pedido, além de clientes criados e fotos gravadas.

**Why this priority**: é a entrega central da HU. Sem ela a vendedora continua digitando pedidos à mão. Todas as demais jornadas dependem desta.

**Independent Test**: com uma vendedora que tem pedidos no +Vendas entre 26/09 e 25/10/2026, importar esse período e conferir que o Ambrosia passa a exibir os mesmos 49 pedidos com total líquido de R$ 123.300,58, que a tela mostrou o progresso bloco a bloco e que o relatório final fecha com as mesmas contagens.

**Acceptance Scenarios**:

1. **Given** uma vendedora autenticada com pedidos do +Vendas já importados até 20/10, **When** abre a tela de importação, **Then** a data inicial vem preenchida com 20/10 e a data final com a data de hoje; os campos de login e senha vêm vazios.
2. **Given** uma vendedora autenticada sem nenhum pedido importado do +Vendas, **When** abre a tela, **Then** a data inicial vem vazia e é obrigatória; a data final vem com a data de hoje.
3. **Given** a tela preenchida com login, senha e um período válido de 30 dias, **When** a vendedora inicia a importação, **Then** a tela confirma o início em poucos segundos, mostra 5 blocos (de 7 dias, o último menor), e atualiza automaticamente o bloco em andamento, os blocos concluídos e as contagens parciais até o fim.
4. **Given** uma importação concluída, **When** a vendedora olha o relatório, **Then** vê identificador, modo, período, duração, resultado, lista de blocos com contagens de inseridos, atualizados, inalterados, não encontrados e com erro, separadas por canal e tipo de pedido, além de clientes criados, imagens gravadas e imagens com erro, com links para os pedidos importados.
5. **Given** data inicial depois da data final, data final no futuro, ou login ou senha vazios, **When** a vendedora tenta iniciar, **Then** o envio é bloqueado com mensagem no campo correspondente; se a mesma requisição chegar ao servidor sem passar pela tela, ela é recusada com erro de validação.
6. **Given** um período maior que o limite configurado (366 dias por padrão), **When** a vendedora tenta iniciar, **Then** recebe uma mensagem clara informando o limite.
7. **Given** um login ou senha recusados pelo +Vendas, **When** a execução começa, **Then** termina como "falhou" com o motivo "credenciais recusadas pelo +Vendas" e nada é gravado.
8. **Given** uma venda que foi cancelada na origem, **When** o período que a contém é importado, **Then** o Ambrosia passa a ter dois registros: a venda (valor positivo) e o cancelamento (valor negativo), como no +Vendas.
9. **Given** o mesmo número de pedido em duas datas diferentes (ex.: `41900001` em 28/set e em 05/out), **When** o período é importado, **Then** o Ambrosia cria dois pedidos distintos.
10. **Given** um usuário não autenticado, **When** tenta abrir a tela de importação, **Then** é redirecionado ao login.

---

### User Story 2 - As credenciais do +Vendas nunca ficam guardadas (Priority: P1)

A vendedora informa login e senha do +Vendas a cada execução. O Ambrosia usa as credenciais só enquanto a execução está em andamento e as descarta no fim (sucesso, falha ou cancelamento). Elas nunca aparecem em banco, arquivo, log, relatório, storage, respostas da API nem no armazenamento do navegador.

**Why this priority**: a senha é de outro sistema, de terceiros. Um vazamento compromete a conta da vendedora no +Vendas. Sem essa garantia a feature não pode ser liberada.

**Independent Test**: executar uma importação com um login e uma senha de teste identificáveis e, depois, procurar esses valores no banco, nos arquivos de log do servidor, no storage, no armazenamento do navegador e em todas as respostas dos endpoints de importação: nenhuma ocorrência.

**Acceptance Scenarios**:

1. **Given** uma importação concluída, **When** se procura o login e a senha usados em todas as tabelas do banco, nos logs do servidor, nos arquivos do servidor e no storage de imagens, **Then** não há nenhuma ocorrência.
2. **Given** qualquer endpoint de importação (iniciar, consultar execução atual, consultar relatório, listar histórico, cancelar), **When** ele responde, **Then** a resposta nunca contém o login ou a senha, inclusive em mensagens de erro de validação.
3. **Given** a tela de importação em uso, **When** se inspeciona o armazenamento do navegador (local, de sessão e cookies), **Then** a senha não está em nenhum deles, o campo de senha não oferece preenchimento automático pelo app e é limpo assim que a execução é iniciada.
4. **Given** uma execução que terminou (com sucesso, falha ou cancelamento) ou um servidor que reiniciou no meio, **When** se examina o que ficou guardado da execução, **Then** só existem o período, o modo, o status, os blocos, as contagens e as mensagens de erro, sem credenciais; da conta do +Vendas, guarda-se apenas o hash com pepper do identificador usado no vínculo.
6. **Given** uma vendedora já vinculada a uma conta do +Vendas, **When** ela inicia uma importação com as credenciais de outra conta, **Then** a execução termina como "falhou" com o motivo "conta do +Vendas diferente da vinculada" e nada é gravado.
5. **Given** um erro de validação na requisição de início, **When** o servidor registra o erro, **Then** o registro não contém o corpo da requisição.

---

### User Story 3 - Repetir uma importação ou um bloco que falhou sem duplicar nada (Priority: P2)

Uma importação pode falhar no meio de um bloco (erro da origem, divergência de contadores, queda do servidor). O que já foi gravado fica; a execução para e o relatório mostra o bloco que falhou, o motivo e quantos pedidos daquele bloco chegaram a entrar. A vendedora repete o bloco (a tela oferece "Repetir este bloco" com as datas já preenchidas) ou o período inteiro, informando as credenciais de novo, e o resultado final é idêntico ao de uma execução que nunca falhou.

**Why this priority**: a origem muda os dados depois do fechamento e a coleta depende de um sistema externo; repetir precisa ser seguro e simples. É o que torna a importação confiável no dia a dia.

**Independent Test**: provocar uma falha no 15º pedido do 3º bloco de uma importação de 5 blocos; confirmar que os 2 primeiros blocos e os 14 pedidos do 3º continuam gravados; repetir o 3º bloco e os seguintes e comparar o estado final com o de uma execução limpa do mesmo período: pedidos, itens e clientes idênticos, sem duplicatas.

**Acceptance Scenarios**:

1. **Given** uma importação de 5 blocos em que o 3º falha no 15º pedido, **When** a execução para, **Then** os blocos 1 e 2 e os 14 pedidos já gravados do bloco 3 continuam no Ambrosia, a execução fica "falhou", e o relatório mostra o bloco 3 como falho, o motivo e "14 pedidos gravados".
2. **Given** o relatório de uma execução com bloco falho, **When** a vendedora aciona "Repetir este bloco", **Then** a tela de importação abre com as datas do bloco preenchidas e os campos de login e senha vazios para serem informados de novo.
3. **Given** o bloco que falhou sendo repetido, **When** a execução termina, **Then** os 14 pedidos já gravados aparecem como "inalterados", os que faltavam aparecem como "inseridos" e nenhum pedido, item ou cliente foi duplicado.
4. **Given** uma importação concluída, **When** o mesmo período é importado de novo sem nenhuma mudança na origem, **Then** o relatório mostra todos os pedidos como "inalterados" e nenhum inserido ou atualizado.
5. **Given** um pedido já importado cujo valor ou status mudou na origem, **When** o período é importado de novo, **Then** o pedido é atualizado, aparece no relatório como "atualizado" e o relatório lista os campos que mudaram.
6. **Given** um bloco cujo contador de pedidos exibido pela origem não bate com as linhas lidas, ou cujo total exibido não bate com a soma dos pedidos lidos, **When** a coleta do bloco termina, **Then** o bloco falha **antes** de gravar qualquer pedido dele, e a execução para.
7. **Given** um pedido que existia na origem numa importação anterior e sumiu dentro do período numa nova importação, **When** o bloco que o contém termina sem falha, **Then** o pedido **não** é apagado e aparece no relatório como "não encontrado nesta execução"; pedidos de blocos que falharam não entram nessa conta.
8. **Given** um pedido importado do +Vendas cujo valor a vendedora editou à mão no Ambrosia, **When** o período é importado de novo, **Then** o valor volta a ser o da origem, o pedido aparece como "atualizado" com o campo alterado listado, e os campos que só existem no Ambrosia continuam intactos.
9. **Given** um pedido cadastrado manualmente no Ambrosia com a mesma chave natural de um pedido do +Vendas, **When** o período é importado, **Then** o pedido manual não é sobrescrito e o relatório mostra um conflito para aquele pedido.

---

### User Story 4 - Simular a importação sem gravar nada (Priority: P2)

A vendedora marca "Simular (não gravar)" para ver o que a importação faria: quantos pedidos seriam inseridos, atualizados e mantidos, quantos clientes seriam criados e quantas fotos seriam gravadas, sem alterar nada no Ambrosia.

**Why this priority**: permite conferir a coleta antes de gravar (útil na primeira execução, em devoluções ainda não inspecionadas e depois de mudanças no +Vendas) sem risco para a base.

**Independent Test**: anotar as contagens de pedidos, clientes e imagens da vendedora; executar uma simulação de um período com pedidos novos; conferir que as contagens não mudaram e que o relatório mostra o que seria inserido, atualizado e mantido.

**Acceptance Scenarios**:

1. **Given** a opção "Simular" marcada, **When** a execução termina, **Then** nenhum pedido, item, cliente ou imagem foi criado ou alterado no Ambrosia.
2. **Given** uma simulação concluída, **When** a vendedora olha o relatório, **Then** ele mostra, por bloco, quantos pedidos seriam inseridos, atualizados e mantidos, quantos clientes seriam criados e quantas fotos seriam gravadas, e é identificado como simulação.
3. **Given** uma simulação, **When** a execução percorre a origem, **Then** aplica as mesmas validações de contadores e totais de uma execução real, e falha o bloco nas mesmas condições.

---

### User Story 5 - Clientes cadastrados sob demanda a partir dos pedidos (Priority: P2)

Não há coleta separada de clientes: quando um pedido traz um CPF que ainda não existe na base da vendedora, o cliente é cadastrado naquele momento, com a mesma proteção de CPF das demais funcionalidades. Se o CPF já existe, o nome é atualizado quando mudou na origem e os demais dados que a vendedora preencheu à mão são preservados.

**Why this priority**: os pedidos importados precisam estar ligados a clientes, e a vendedora não deve cadastrar clientes à mão antes de importar.

**Independent Test**: importar um período com um pedido de CPF novo e três pedidos de um mesmo CPF espalhados em dois blocos; conferir que o CPF novo virou um cliente com CPF protegido e mascarado, e que o CPF repetido tem um único cliente ligado aos três pedidos.

**Acceptance Scenarios**:

1. **Given** um pedido cujo CPF não existe na base da vendedora, **When** é importado, **Then** o cliente é cadastrado com o CPF protegido (hash para unicidade, cifra para recuperação, versão mascarada para exibição) e o pedido fica ligado a ele.
2. **Given** o mesmo CPF em vários pedidos, no mesmo bloco ou em blocos diferentes, **When** o período é importado, **Then** existe um único cliente para esse CPF e todos os pedidos apontam para ele.
3. **Given** um cliente já existente cujo nome mudou na origem, **When** o pedido é importado, **Then** o nome é atualizado e os demais dados preenchidos manualmente (telefone, observações, fotos etc.) permanecem intactos.
4. **Given** uma simulação, **When** termina, **Then** o relatório mostra quantos clientes seriam criados, e nenhum foi criado.
5. **Given** um CPF em qualquer registro de log ou relatório, **When** se busca pelo padrão de CPF, **Then** só aparece a versão mascarada.

---

### User Story 6 - Fotos dos produtos copiadas para o storage (Priority: P3)

Cada foto de item de pedido é baixada da origem e gravada no storage de imagens do Ambrosia, na área da vendedora, identificada como vinda da importação; o item fica ligado à imagem e a URL de origem é mantida. A mesma foto em vários pedidos é gravada uma vez só. Uma foto que falha não impede a gravação do pedido.

**Why this priority**: as fotos deixam o histórico de vendas mais útil (reconhecer peças, montar banco de fotos), mas a importação de pedidos e clientes já entrega valor sem elas.

**Independent Test**: importar um período com um produto repetido em três pedidos e um item cuja foto responde erro na origem; conferir que o produto repetido tem uma única imagem no storage ligada aos três itens, que o pedido do item com erro foi gravado com o item sem imagem e que o relatório lista a foto como "imagem com erro".

**Acceptance Scenarios**:

1. **Given** um item de pedido com foto na origem, **When** o pedido é importado, **Then** a foto é gravada no storage na área da vendedora com origem "importação", o item fica ligado à imagem e a consulta da imagem devolve um link de leitura que abre a foto.
2. **Given** a mesma foto em vários pedidos, **When** o período é importado, **Then** a foto é gravada uma única vez e todos os itens apontam para a mesma imagem.
3. **Given** uma foto cuja URL de origem responde erro, **When** o pedido é importado, **Then** o pedido e o item são gravados, o item fica sem imagem, o relatório lista a foto como "imagem com erro", e uma nova importação do mesmo período tenta baixá-la de novo.
4. **Given** uma foto de tipo fora dos aceitos pelo storage (ex.: GIF) ou acima do limite de tamanho, **When** o pedido é importado, **Then** a foto é ignorada e reportada, e o pedido é gravado normalmente.
5. **Given** um item já importado que tem imagem ligada e cuja URL de origem não mudou, **When** o período é importado de novo, **Then** a foto não é baixada nem gravada outra vez.
6. **Given** uma simulação, **When** termina, **Then** nenhuma foto foi gravada no storage e o relatório mostra quantas seriam gravadas.

---

### User Story 7 - Acompanhar, retomar, cancelar e consultar o histórico das importações (Priority: P3)

A importação continua no servidor mesmo que a vendedora feche a aba. Ao voltar à tela, ela vê a execução em andamento (se houver) ou o último relatório. Ela pode cancelar uma execução em andamento (o cancelamento vale ao fim do bloco atual) e consultar as últimas execuções. Duas execuções simultâneas da mesma vendedora não acontecem.

**Why this priority**: uma importação pode levar vários minutos; a vendedora precisa poder sair e voltar, e a base precisa ficar protegida contra execuções concorrentes. Complementa a jornada principal.

**Independent Test**: iniciar uma importação, fechar a aba, reabrir a tela e ver a execução em andamento; abrir uma segunda aba e tentar iniciar outra importação: recusada com "já existe importação em andamento"; cancelar na primeira aba e ver que a execução termina ao fim do bloco atual com os blocos já concluídos preservados.

**Acceptance Scenarios**:

1. **Given** uma importação em andamento, **When** a vendedora fecha a aba e reabre a tela, **Then** a execução continuou no servidor e a tela volta a mostrar o progresso dela.
2. **Given** nenhuma importação em andamento, **When** a vendedora abre a tela, **Then** vê o formulário com as datas sugeridas e o último relatório, se existir, com destaque quando o resultado foi "falhou" ou "parcial".
3. **Given** uma importação em andamento da mesma vendedora, **When** ela tenta iniciar outra (em outra aba, por exemplo), **Then** a segunda é recusada com a mensagem "já existe importação em andamento" e a primeira continua.
4. **Given** uma importação em andamento, **When** a vendedora cancela, **Then** a execução termina ao fim do bloco atual, os blocos concluídos ficam gravados, a execução é marcada como cancelada e as credenciais são descartadas.
5. **Given** execuções anteriores, **When** a vendedora consulta o histórico, **Then** vê as últimas execuções, paginadas, com período, modo, resultado, duração e contagens, e pode abrir o relatório de cada uma.
6. **Given** uma importação cujo período já foi importado antes, **When** a nova execução termina com menos pedidos que a anterior no mesmo período, **Then** a tela exibe um aviso de queda na contagem.
7. **Given** uma execução interrompida pela reinicialização do servidor, **When** a vendedora volta à tela, **Then** a execução aparece como "falhou" com motivo de interrupção, e repetir o período é seguro.

---

### Edge Cases

- **Período de um único dia** (data inicial igual à final): gera um único bloco de um dia.
- **Período que não é múltiplo do tamanho do bloco**: o último bloco é menor; nenhum dia fica de fora nem se repete entre blocos.
- **Período sem nenhum pedido na origem**: a execução termina com sucesso, todos os blocos com contagens zero, sem erro.
- **Pedido de datas diferentes com o mesmo número**: são pedidos distintos (a identidade inclui a data e o tipo).
- **Listagem da origem sem o ano na data** (`24 Out`): o ano é deduzido pelo período do bloco e confirmado pelo detalhe do pedido; divergência falha o bloco.
- **Virada de ano dentro do período**: as datas são resolvidas por bloco, sem atribuir o ano errado a pedidos de dezembro/janeiro.
- **Pedido com itens alterados na origem** (item removido ou adicionado): o pedido é atualizado e os itens passam a refletir a origem; os itens anteriores são substituídos, não acumulados.
- **Pedido sem CPF ou com CPF inválido na origem**: o pedido é reportado como "com erro" naquele bloco e os demais pedidos do bloco seguem; a execução não para por isso.
- **Devolução** (tipo ainda não inspecionado em detalhe na origem): tratada como pedido de tipo "devolução" com valor negativo; a primeira execução recomendada é em modo simulação.
- **Origem instável** ("Erro ao carregar…"): a coleta tenta de novo com espera crescente e aborta o bloco depois de 3 falhas consecutivas, marcando-o como falho.
- **Credenciais recusadas repetidamente**: na 3ª recusa seguida, a vendedora fica 15 minutos sem poder iniciar nova importação, com mensagem indicando o horário de liberação; isso evita bloquear a conta dela no +Vendas e impede o uso da tela para testar senhas.
- **Origem com segundo fator ou captcha no login**: a execução falha com mensagem clara indicando que o login exige uma etapa não suportada; nada é gravado.
- **Servidor com limite global de execuções atingido** (outras vendedoras importando): a nova execução fica aguardando na fila e a tela informa "aguardando" até começar.
- **Cancelamento pedido durante o último bloco**: o bloco termina normalmente e a execução é marcada como cancelada sem pedidos pendentes.
- **Foto que exige a sessão do +Vendas para ser baixada**: a coleta baixa a foto com a mesma sessão autenticada; se ainda assim falhar, aplica-se a regra de "imagem com erro".
- **Mudança na interface da origem**: a validação por contadores e totais falha o bloco em vez de gravar dados errados ou parciais.

## Requirements *(mandatory)*

### Functional Requirements

**Tela e parâmetros**

- **FR-001**: O sistema DEVE oferecer uma tela de importação acessível só a usuários autenticados, com os campos login do +Vendas, senha do +Vendas (mascarada, sem opção "lembrar"), data inicial, data final e a opção "Simular (não gravar)". Usuário sem sessão é redirecionado ao login.
- **FR-002**: Ao abrir a tela, o sistema DEVE sugerir como data inicial a data do último pedido da vendedora importado do +Vendas (vazia e obrigatória quando não há nenhum) e como data final a data de hoje no fuso da operação (América/São Paulo).
- **FR-003**: O sistema DEVE recusar, na tela e no servidor, importações com data inicial depois da final, data final no futuro, login ou senha vazios, ou período maior que o limite configurado (padrão 366 dias), com mensagem clara no campo correspondente.
- **FR-004**: A importação DEVE abranger os pedidos com data entre a data inicial e a data final, ambas inclusive.

**Credenciais**

- **FR-005**: O sistema DEVE usar o login e a senha do +Vendas apenas em memória, durante a execução, e descartá-los ao fim dela (sucesso, falha ou cancelamento).
- **FR-006**: O sistema NUNCA DEVE gravar o login ou a senha do +Vendas em banco, arquivo, log, relatório, storage, respostas de qualquer endpoint de importação (inclusive mensagens de erro de validação) ou armazenamento do navegador (local, de sessão ou cookies). A única informação derivada que pode ser guardada é o hash com pepper do identificador da conta (FR-008b), que não permite recuperar o login.
- **FR-008b**: Na primeira execução em modo gravação com login aceito, o sistema DEVE vincular a vendedora à conta do +Vendas guardando apenas um hash com pepper do identificador da conta na origem (o código da vendedora exibido pelo +Vendas ou, se não houver, o login normalizado). Nas execuções seguintes, inclusive simulações, uma conta cujo hash não bate DEVE encerrar a execução como "falhou", com o motivo "conta do +Vendas diferente da vinculada", antes de gravar qualquer pedido, cliente ou imagem. Desfazer o vínculo fica a cargo do responsável técnico nesta entrega.
- **FR-007**: A tela DEVE limpar o campo de senha assim que a execução é iniciada e NÃO DEVE oferecer preenchimento automático da senha pelo próprio aplicativo.
- **FR-008**: Login ou senha recusados pelo +Vendas DEVEM encerrar a execução como "falhou", com o motivo "credenciais recusadas pelo +Vendas", sem gravar nenhum pedido, cliente ou imagem.
- **FR-008a**: Após 3 execuções seguidas da mesma vendedora terminadas com "credenciais recusadas pelo +Vendas", o sistema DEVE recusar novos inícios por 15 minutos (valores configuráveis), na tela e no servidor, com mensagem que informe quando será possível tentar de novo; a contagem zera quando um login no +Vendas é aceito. O controle não guarda o login nem a senha.

**Execução em blocos**

- **FR-009**: O sistema DEVE dividir o período em blocos consecutivos de datas de tamanho configurável (padrão 7 dias corridos), da data inicial para a final, sem sobreposição e sem lacunas; o último bloco pode ser menor.
- **FR-010**: Para cada bloco, o sistema DEVE, nesta ordem: coletar todos os pedidos do bloco; validar que o contador de pedidos exibido pela origem bate com as linhas lidas e que o total exibido bate com a soma dos pedidos lidos; só então gravar pedido a pedido.
- **FR-011**: Divergência de contador ou de total DEVE falhar o bloco antes de qualquer gravação dele e encerrar a execução.
- **FR-012**: Cada pedido (com seu cliente sob demanda e seus itens) DEVE ser gravado de forma atômica e independente: um pedido nunca fica gravado pela metade, e a falha em um pedido não desfaz os pedidos já gravados do mesmo bloco nem dos anteriores.
- **FR-013**: Quando um bloco falha, a execução DEVE parar, o bloco DEVE ficar como "falhou" com o motivo e a contagem de pedidos que chegaram a ser gravados, e os blocos seguintes DEVEM ficar como não executados.
- **FR-014**: A tela DEVE mostrar, durante a execução, o bloco em andamento, quantos blocos já terminaram, as contagens parciais e o resultado de cada bloco, atualizados automaticamente sem a vendedora recarregar a página.
- **FR-015**: Enquanto a execução durar, a tela DEVE refletir o progresso com atraso máximo de 5 segundos.
- **FR-016**: O sistema DEVE recusar o início de uma nova importação enquanto a mesma vendedora tiver outra em andamento, com a mensagem "já existe importação em andamento", sem afetar a execução em curso.
- **FR-017**: O sistema DEVE limitar o número de importações simultâneas de vendedoras diferentes (padrão 2); as demais aguardam na fila com status "aguardando" visível na tela.
- **FR-018**: A execução DEVE continuar no servidor quando a vendedora fecha a aba; ao voltar à tela, o sistema DEVE mostrar a execução em andamento ou, na ausência dela, o último relatório.
- **FR-019**: A vendedora DEVE poder cancelar uma execução em andamento; o cancelamento vale ao fim do bloco atual, preserva os blocos concluídos, marca a execução como cancelada e descarta as credenciais.
- **FR-020**: Uma execução interrompida por reinicialização do servidor DEVE ficar como "falhou" com motivo de interrupção, e repetir o período DEVE ser seguro.

**Idempotência**

- **FR-021**: Um pedido importado DEVE ser identificado pela chave natural (vendedora, número do pedido, data do pedido, tipo), a mesma unicidade já vigente no Ambrosia.
- **FR-022**: O sistema DEVE detectar mudanças por uma impressão digital do conteúdo normalizado do pedido (campos do pedido, identificação protegida do cliente e itens em ordem fixa), que NÃO inclui a data da captura nem a imagem gravada.
- **FR-023**: Na gravação, pedido inexistente DEVE ser inserido; pedido existente com o mesmo conteúdo DEVE ter apenas a data da última captura atualizada e contar como "inalterado"; pedido existente com conteúdo diferente DEVE ser atualizado, com os itens substituídos pelos da origem, registrando a data da mudança e a lista de campos alterados, e contar como "atualizado". Em pedidos de origem "+Vendas", a origem prevalece sobre edições manuais feitas no Ambrosia nos campos vindos da origem (inclusive os itens); os campos que só existem no Ambrosia são preservados. Uma edição manual, no Ambrosia, de um pedido de origem "+Vendas" invalida a impressão digital gravada. Na reimportação seguinte, o pedido volta a receber os valores da origem e conta como "atualizado", com a lista de campos alterados comparada com o conteúdo gravado.
- **FR-024**: Repetir a mesma importação sem mudança na origem NÃO DEVE criar nem alterar nenhum pedido, item, cliente ou imagem; todos os pedidos contam como "inalterados".
- **FR-025**: Repetir um bloco que falhou DEVE completar a importação sem duplicar nada, com resultado final idêntico ao de uma execução sem falha.
- **FR-026**: O relatório de uma execução com bloco falho DEVE oferecer "Repetir este bloco", que abre a tela com as datas do bloco preenchidas e exige login e senha de novo.
- **FR-027**: Pedidos importados antes e ausentes da origem dentro do período NÃO DEVEM ser apagados nem alterados; DEVEM ser listados no relatório como "não encontrados nesta execução", considerando apenas blocos que terminaram sem falha.
- **FR-028**: Pedidos cadastrados manualmente no Ambrosia com a mesma chave natural de um pedido da origem NÃO DEVEM ser sobrescritos; o conflito DEVE constar no relatório.
- **FR-029**: Uma venda e o cancelamento dela DEVEM gerar dois registros distintos (venda com valor positivo e cancelamento com valor negativo), como na origem; devoluções seguem a mesma regra com tipo "devolução".
- **FR-030**: Cada pedido importado DEVE registrar a origem "+Vendas", o identificador externo do pedido na origem quando disponível, a data e hora da última captura e a data e hora da última mudança de conteúdo.
- **FR-031**: A normalização DEVE converter valores monetários em formato brasileiro para decimal com duas casas, manter o CPF só com dígitos e deduzir o ano das datas pelo período do bloco, confirmado pelo detalhe do pedido.

**Simulação**

- **FR-032**: Em modo "Simular", o sistema DEVE percorrer a origem com as mesmas validações, mas NÃO DEVE gravar nem alterar pedidos, itens, clientes ou imagens; o relatório DEVE mostrar o que seria inserido, atualizado e mantido, quantos clientes seriam criados e quantas fotos seriam gravadas, identificando a execução como simulação.

**Clientes**

- **FR-033**: Um CPF que ainda não existe na base da vendedora DEVE ser cadastrado como cliente no primeiro pedido importado em que aparece, com a mesma proteção já vigente (hash com pepper para unicidade, cifra autenticada para recuperação e versão mascarada para exibição).
- **FR-034**: O mesmo CPF em vários pedidos, no mesmo bloco ou em blocos diferentes, DEVE resultar em um único cliente.
- **FR-035**: Para cliente já existente, o sistema DEVE atualizar o nome quando mudou na origem e preservar todos os demais dados preenchidos manualmente.
- **FR-036**: Pedido sem CPF ou com CPF inválido na origem DEVE ser reportado como "com erro" sem interromper os demais pedidos do bloco.

**Fotos dos produtos**

- **FR-037**: Cada foto de item DEVE ser baixada da origem (com a sessão autenticada da coleta, quando exigida) e gravada no storage de imagens na área da vendedora, identificada como vinda da importação, com deduplicação por conteúdo; o item DEVE ficar ligado à imagem e manter a URL de origem.
- **FR-038**: A mesma foto em vários pedidos DEVE ser gravada uma única vez.
- **FR-039**: Falha ao baixar uma foto, tipo fora dos aceitos pelo storage ou tamanho acima do limite NÃO DEVEM falhar o bloco: o pedido e o item são gravados, o item fica sem imagem, e a foto é listada no relatório como "imagem com erro"; uma nova importação tenta de novo.
- **FR-040**: O sistema NÃO DEVE baixar nem gravar de novo a foto de um item que já tem imagem ligada e cuja URL de origem não mudou.
- **FR-041**: Fotos gravadas antes da falha do pedido correspondente DEVEM ser reaproveitadas pela deduplicação por conteúdo na repetição do bloco: a foto pode ser baixada de novo, mas NÃO DEVE gerar novo objeto no storage nem nova imagem.

**Segurança da coleta**

- **FR-042**: A coleta DEVE ser somente leitura na origem: NUNCA acionar ações que criem, alterem ou excluam dados (ex.: "Novo carrinho", "Sair") e NUNCA acessar rotas ou acionar elementos fora de uma lista explícita de permitidos; o menu BOX não é seguido.
- **FR-043**: A coleta DEVE respeitar o ritmo máximo de 1 a 2 requisições por segundo à origem, com pausas aleatórias; o download de fotos conta no mesmo limite.
- **FR-044**: A coleta DEVE usar uma sessão nova por execução, sem reaproveitar sessão ou cookies entre execuções e sem persistir a sessão em disco; a sessão é encerrada ao fim da execução.
- **FR-045**: Erros transitórios da origem DEVEM ser tratados com novas tentativas e espera crescente; o bloco é abortado após 3 falhas consecutivas. Falhas no download de fotos seguem a FR-039 e não abortam o bloco.
- **FR-046**: Login da origem que exija segundo fator ou captcha DEVE encerrar a execução como "falhou", com mensagem clara, sem gravar nada.

**Relatório, histórico e registro**

- **FR-047**: Cada execução DEVE ser registrada com identificador, modo (simulação ou gravação), período, tamanho do bloco, início, fim, duração, resultado (aguardando, em andamento, concluída, falhou, parcial, cancelada), contagens totais (inseridos, atualizados, inalterados, não encontrados, com erro, clientes criados, imagens gravadas, imagens com erro) e mensagem de erro, sem credenciais.
- **FR-047a**: O resultado final DEVE seguir esta regra: "concluída" quando todos os blocos terminaram sem falha e não houve pedido com erro, conflito com pedido manual nem imagem com erro; "parcial" quando todos os blocos terminaram sem falha, mas houve pelo menos um desses casos; "falhou" quando um bloco falhou, as credenciais foram recusadas, a conta do +Vendas difere da vinculada, o login exigiu etapa não suportada ou a execução foi interrompida; "cancelada" quando a vendedora cancelou.
- **FR-048**: Cada bloco DEVE ser registrado com número, período, resultado, contagens, mensagem de erro, início e fim; cada pedido inserido, atualizado ou não encontrado DEVE ser registrado com sua chave natural, a ação e, para atualizações, os campos alterados.
- **FR-049**: O relatório DEVE ser consultável a qualquer momento depois da execução, com contagens por bloco, canal e tipo de pedido, e com links para os pedidos importados.
- **FR-050**: A vendedora DEVE poder listar suas últimas execuções, paginadas, e abrir o relatório de cada uma; execuções "falhou" ou "parcial" aparecem em destaque na tela.
- **FR-051**: A tela DEVE avisar quando a contagem de pedidos de um período cair em relação à execução anterior que cobriu o mesmo período.
- **FR-052**: Os registros operacionais da execução DEVEM conter identificador da execução, bloco, rota visitada na origem, contagens e tempos, e NUNCA conter CPF completo, login ou senha; CPF só aparece mascarado em qualquer log ou relatório.
- **FR-053**: Toda consulta de execução, bloco ou relatório DEVE ser restrita à vendedora dona; execução de outra vendedora responde como inexistente.

### Key Entities *(include if feature involves data)*

- **Execução de importação**: um pedido da vendedora para importar um período, com modo (simulação ou gravação), período, tamanho de bloco, status (aguardando, em andamento, concluída, falhou, parcial, cancelada), início, fim, contagens totais, mensagem de erro e versão do coletor. Pertence a uma vendedora. Nunca guarda credenciais.
- **Vínculo com a conta do +Vendas**: um por vendedora, com o hash com pepper do identificador da conta na origem e a data do vínculo; nunca guarda o login nem a senha. Também guarda a contagem de recusas seguidas de credenciais e o horário até o qual novos inícios ficam bloqueados (FR-008a).
- **Bloco de importação**: uma fatia consecutiva de datas de uma execução, numerada, com status próprio, contagens (inseridos, atualizados, inalterados, não encontrados, com erro, clientes criados, imagens gravadas, imagens com erro), mensagem de erro, início e fim. Pertence a uma execução.
- **Alteração registrada**: o rastro de cada pedido tocado por uma execução: chave natural do pedido, ação (inserido, atualizado, não encontrado, conflito com manual), campos alterados e, quando gravado, referência ao pedido. Pertence a uma execução e a um bloco; em simulação não aponta para pedido gravado.
- **Pedido** (já existente): ganha a origem (manual ou +Vendas), a impressão digital do conteúdo normalizado, a data e hora da última captura e a da última mudança de conteúdo; identificado por (vendedora, número, data, tipo). Mantém a relação com cliente e itens.
- **Item de pedido** (já existente): ganha a ligação opcional com a imagem gravada no storage, mantendo a URL de origem da foto.
- **Cliente** (já existente): identificado por (vendedora, CPF protegido); criado sob demanda pela importação e com nome atualizável pela origem.
- **Imagem armazenada** (já existente, feature 004): foto de produto gravada com origem "importação", deduplicada por conteúdo na área da vendedora.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Após importar o período de 26/09 a 25/10/2026, o Ambrosia exibe exatamente 49 pedidos com total líquido de R$ 123.300,58, iguais à tela do +Vendas.
- **SC-002**: Repetir uma importação sem mudança na origem resulta em 0 pedidos inseridos, 0 atualizados e 100% inalterados, sem nenhum registro novo de pedido, item, cliente ou imagem.
- **SC-003**: Depois de uma falha no meio de um bloco e da repetição desse bloco, o conjunto de pedidos, itens e clientes é idêntico ao de uma execução sem falha do mesmo período: 0 duplicatas e 0 pedidos faltando.
- **SC-004**: Após uma execução completa, há 0 ocorrências do login, da senha e de CPF completo no banco, nos logs, nos arquivos do servidor, no storage, nas respostas dos endpoints de importação e no armazenamento do navegador.
- **SC-005**: A vendedora inicia uma importação em menos de 1 minuto a partir da abertura da tela, informando apenas login e senha quando as datas sugeridas servem.
- **SC-006**: Durante a execução, a tela reflete o progresso com atraso máximo de 5 segundos, e a confirmação de início aparece em até 3 segundos após o envio.
- **SC-007**: Um mês de cota típico (cerca de 50 pedidos) é importado por completo, com fotos, em até 15 minutos, respeitando o ritmo máximo de 2 requisições por segundo à origem.
- **SC-008**: 100% das tentativas de iniciar uma segunda importação enquanto outra da mesma vendedora está em andamento são recusadas com a mensagem "já existe importação em andamento".
- **SC-009**: 100% dos blocos com divergência de contador ou de total são marcados como falhos sem nenhum pedido daquele bloco gravado.
- **SC-010**: 100% dos pedidos cujas fotos falharam são gravados mesmo assim, com a foto listada como "imagem com erro"; a mesma foto presente em N pedidos ocupa 1 objeto no storage.
- **SC-011**: Em modo simulação, as contagens de pedidos, clientes e imagens da vendedora são idênticas antes e depois da execução.
- **SC-012**: A coleta nunca acessa rota fora da lista de permitidos: 0 ocorrências em auditoria de navegação após uma execução completa.

## Assumptions

- **Tamanho do bloco**: 7 dias corridos por padrão, configurável pelo responsável técnico; a divisão por mês de cota (26 a 25) não é adotada nesta entrega (pergunta 2 da HU).
- **Data inicial sem histórico**: vem vazia e obrigatória, conforme o critério 3 da HU; não se pré-preenche com o início do mês de cota (pergunta 3 da HU).
- **Pedidos que somem da origem**: apenas reportados como "não encontrados nesta execução"; nunca apagados nem marcados como removidos nesta entrega (pergunta 4 da HU).
- **Conflito com pedido manual**: o pedido manual não é sobrescrito e o conflito é reportado (proposta da HU, pergunta 5).
- **Segundo fator ou captcha no +Vendas**: assume-se que o login é só com login e senha; se a origem exigir etapa adicional, a execução falha com mensagem clara e uma etapa de código fica para evolução futura (pergunta 1 da HU).
- **Dados da origem**: assume-se que a resposta interna de detalhe do pedido do +Vendas traz cliente, CPF, itens e fotos; o que faltar é lido da tela. Confirmar na primeira execução em simulação (pergunta 6 da HU).
- **URLs das fotos**: assume-se que podem exigir a sessão do +Vendas; a coleta baixa as fotos com a própria sessão autenticada (pergunta 7 da HU).
- **Retenção do histórico de execuções**: sem limpeza automática nesta entrega; o histórico fica disponível indefinidamente. O payload bruto da origem **não** é guardado (contém CPF e não há uso imediato) (pergunta 8 da HU).
- **Limite de execuções simultâneas no servidor**: 2 por padrão, configurável; execuções além do limite aguardam na fila.
- **Fuso da operação**: America/Sao_Paulo para a data de hoje e para os limites de período.
- **Canal e tipo**: os pedidos vêm dos canais Cota e Omni, com os tipos venda, devolução e cancelamento; Pedidos Extras e Carrinhos ficam fora do escopo.
- **Termos de uso**: a automação de leitura do +Vendas é aceita pelos termos da Somalabs; a conferência fica registrada como pré-requisito de liberação.
- **Testes com origem simulada**: as verificações que exigem provocar falhas, divergências ou mudanças na origem (falha no 15º pedido, contador divergente, foto com erro, nome alterado) assumem a existência de uma origem de teste controlada, já que o +Vendas real não pode ser manipulado.
- **Dependências**: feature 001 (modelo, proteção do CPF e regras de pedido), feature 002 (frontend e padrão das telas), feature 003 (autenticação por e-mail e senha que protege a tela e os endpoints) e feature 004 (storage de imagens com gravação pelo servidor, deduplicação e origem "bot/importação"). A HU 008 (OAuth/MCP) deixa de ser dependência.
- **Alinhamento com a constituição**: a proibição de automação de navegador vale para **testes do frontend**; a coleta somente leitura de um sistema externo, executada no servidor, é funcionalidade de produto e não teste, e será avaliada no "Constitution Check" do plano.

## Fora do escopo

- KPIs de cota, supercota, PA, VA e TMA (ficam para HU futura).
- Execução agendada ou sem a vendedora presente (exigiria guardar a senha).
- Guardar sessão ou cookies do +Vendas entre execuções.
- Pedidos Extras e Carrinhos.
- Qualquer escrita no +Vendas.
- Guardar o payload bruto da origem.
- Etapa de segundo fator ou captcha na tela.
