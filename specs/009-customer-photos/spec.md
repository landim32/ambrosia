# Feature Specification: Fotos de clientes no estilo rede social

**Feature Branch**: `009-customer-photos`
**Created**: 2026-10-29
**Status**: Draft
**Input**: User description: "Melhoria no cliente: o cadastro de clientes deve ter um cadastro de fotos. Essas fotos podem ter uma descrição. Deve funcionar parecido com uma rede social. O cliente pode ter uma foto de perfil, essa foto aparece redonda no lugar das iniciais do cliente. No frontend, deve aparecer da mesma forma que aparece no Instagram, tem uma aba fotos e outra para pedidos. Prioridade nas fotos. O usuário ou o MCP pode marcar aquela foto com um coração, de favorita. A foto do perfil deve vir dessa lista de fotos, ela deve ser quadrada focando o rosto, no frontend deve aparecer redonda. Mas ela deve ter um link com a foto original. As fotos devem aparecer ajustadas no frontend, mas devem manter as proporções originais no arquivo. Essas fotos não são importadas, serão cadastradas pelo MCP e API. No frontend deve ter um campo discreto na aba fotos "arraste para incluir uma foto", aí já inclui sem descrição. Na navegação o usuário pode editar a descrição de qualquer foto."

## Revisão de escopo — 2026-10-29 (antes da implementação)

A versão original desta spec supunha que o **servidor** processa a imagem: normalizar o arquivo, remover
EXIF/GPS, gerar miniatura para a grade e recortar o quadrado do perfil. Essa base foi revista por custo de
licença e superfície de vulnerabilidade de biblioteca de decodificação, e o usuário decidiu:

- **nenhum processamento de imagem no servidor**;
- a grade de fotos mostra o **arquivo original** (sem miniatura);
- o quadrado da foto de perfil é gerado **no navegador** e enviado como arquivo;
- o metadado EXIF/GPS **permanece** no arquivo guardado;
- a validação de upload da feature 004 fica **intacta** (tipo declarado + tamanho + SHA-256).

FRs afetadas: **FR-003** (reescrita), **FR-004** (reescrita), **FR-005** (revogada), **FR-012** e
**FR-013** (recorte no navegador, enquadramento não persistido), **SC-003** (reescrito) e os edge cases de
localização e orientação. Nada mais da spec mudou: álbum, descrição, favorita, abas, isolamento, URLs
temporárias, canais e paridade continuam como pedidos. A justificativa medida está em
[research.md](research.md).

## Contexto

Hoje o perfil da cliente mostra um círculo com as iniciais e a lista de pedidos. A infraestrutura de imagens (feature 004) já guarda arquivos num storage privado, acessados só por links temporários e isolados por vendedora, mas nenhuma imagem está ligada a uma cliente.

Esta feature cria o **álbum de fotos da cliente**, inspirado no perfil de uma rede social:

- uma foto de perfil redonda no lugar das iniciais, escolhida entre as fotos da cliente;
- uma grade de fotos como aba principal do perfil, com a aba de pedidos ao lado;
- descrição e marcação de favorita (coração) em cada foto;
- inclusão de fotos pela tela, por arraste, e pela conversa no ChatGPT (MCP).

Ela realiza a história de usuário 010 ("Banco de imagens de clientes alimentado pelo ChatGPT") e a amplia. A HU 010 deixava fora do escopo o envio de fotos pela tela e o recorte de imagem, e esta feature traz os dois. As fotos **não** vêm da importação do +Vendas.

## Clarifications

### Session 2026-10-29

- Q: O enquadramento no rosto é automático, manual ou automático com ajuste? → A: Manual. A vendedora posiciona um quadrado sobre a foto ao escolher o perfil. O sistema não detecta rostos.
- Q: Exigir consentimento registrado da cliente antes de guardar fotos (HU 010, LGPD)? → A: Não exigir. O consentimento fica sob responsabilidade da vendedora e é tratado na política de privacidade, sem bloquear a inclusão.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ver as fotos da cliente numa grade, na aba principal do perfil (Priority: P1)

A vendedora abre o perfil de uma cliente e vê, logo abaixo do cabeçalho, duas abas: **Fotos** (aberta por padrão) e **Pedidos**. A aba Fotos mostra as fotos numa grade de quadrados, a mais recente primeiro, como num perfil de rede social. Ao tocar numa foto, ela abre ampliada, inteira e sem cortes, com a descrição, a data e o coração de favorita. Na foto ampliada, a vendedora pode passar para a anterior e a próxima.

**Why this priority**: é o centro da feature. Sem a grade e a foto ampliada, nenhuma das outras histórias tem onde aparecer.

**Independent Test**: com uma cliente que já tem fotos de formatos variados (retrato, paisagem e quadrada), abrir o perfil. Conferir que a aba Fotos vem aberta, que a grade mostra recortes quadrados sem distorção, que a foto ampliada aparece inteira na proporção original, e que a aba Pedidos continua mostrando os pedidos como antes.

**Acceptance Scenarios**:

1. **Given** uma cliente com fotos, **When** a vendedora abre o perfil, **Then** a aba **Fotos** está selecionada e mostra a grade, da foto mais recente para a mais antiga.
2. **Given** a grade de fotos, **When** uma foto não é quadrada, **Then** a célula a preenche em quadrado sem esticar nem achatar (recorte central só na exibição, sobre o arquivo original), e o arquivo guardado continua com a proporção original.
3. **Given** a grade, **When** a vendedora toca numa foto, **Then** a foto abre ampliada, inteira, na proporção original, com a descrição (se houver), a data de inclusão e o coração.
4. **Given** uma foto ampliada, **When** a vendedora avança ou volta, **Then** vê a próxima ou a anterior na ordem da grade, sem fechar a visualização.
5. **Given** o perfil de uma cliente, **When** a vendedora escolhe a aba **Pedidos**, **Then** vê os pedidos da cliente como hoje.
6. **Given** uma cliente sem fotos, **When** a vendedora abre o perfil, **Then** a aba Fotos continua sendo a padrão e mostra uma mensagem de que ainda não há fotos, junto com a área de inclusão.
7. **Given** uma cliente com muitas fotos, **When** a vendedora rola a grade, **Then** as fotos seguintes carregam conforme ela rola, sem travar a tela.

---

### User Story 2 - Incluir fotos arrastando para a aba Fotos (Priority: P1)

Na aba Fotos há uma área discreta escrita **"Arraste para incluir uma foto"**. A vendedora arrasta uma ou mais fotos do computador para essa área, e elas entram no álbum na hora, sem descrição e sem nenhuma pergunta. No celular, onde não dá para arrastar, tocar na área abre o seletor de fotos do aparelho.

**Why this priority**: sem um jeito de incluir fotos pela tela, o álbum depende só da conversa no ChatGPT. O arraste é o caminho mais rápido para a vendedora alimentar o perfil.

**Independent Test**: arrastar três fotos para a área de inclusão. Conferir que as três aparecem no topo da grade sem pedir descrição, que o arquivo guardado de cada uma tem a mesma proporção do original e que um arquivo que não é imagem é recusado com uma mensagem clara.

**Acceptance Scenarios**:

1. **Given** a aba Fotos aberta, **When** a vendedora arrasta uma foto JPEG, PNG ou WebP para a área de inclusão, **Then** a foto é incluída sem descrição e aparece no início da grade.
2. **Given** a aba Fotos, **When** a vendedora arrasta várias fotos de uma vez, **Then** todas são incluídas, cada uma com seu próprio indicador de progresso.
3. **Given** um envio em andamento, **When** uma das fotos falha (tipo não aceito, acima do tamanho máximo ou erro de rede), **Then** só ela mostra o erro com o motivo, e as demais seguem normalmente.
4. **Given** a tela num celular, **When** a vendedora toca na área de inclusão, **Then** abre o seletor de fotos do aparelho e as fotos escolhidas são incluídas do mesmo jeito.
5. **Given** uma foto idêntica a outra que já está no álbum da cliente, **When** é incluída de novo, **Then** não gera uma segunda cópia, e a vendedora é avisada de que a foto já estava no álbum.
6. **Given** a área de inclusão, **When** nada está sendo arrastado, **Then** ela aparece discreta, sem competir visualmente com as fotos.

---

### User Story 3 - Foto de perfil redonda, escolhida entre as fotos da cliente (Priority: P1)

A vendedora escolhe uma das fotos da cliente como **foto de perfil** e posiciona um quadrado sobre o rosto da cliente, aumentando ou diminuindo o quadrado se precisar. O navegador produz o arquivo quadrado daquele enquadramento e o sistema o guarda, e ele aparece redondo no lugar das iniciais: no cabeçalho do perfil, na lista de clientes e nos demais lugares onde hoje aparecem as iniciais. Tocar na foto de perfil abre a foto original, inteira. *(Redação ajustada na revisão de 2026-10-29: quem recorta é o navegador, não o sistema — ver FR-012.)*

**Why this priority**: é o elemento mais visível da feature. Faz a cliente ser reconhecida de relance na lista e no perfil.

**Independent Test**: escolher como perfil uma foto em pé, de corpo inteiro, e posicionar o quadrado sobre o rosto. Conferir que o círculo mostra o rosto da cliente, que a lista de clientes passa a mostrar o círculo no lugar das iniciais, e que tocar no círculo abre a foto original inteira.

**Acceptance Scenarios**:

1. **Given** uma foto da cliente ampliada, **When** a vendedora escolhe "usar como foto de perfil", **Then** a tela mostra um quadrado sobre a foto, inicialmente o maior quadrado possível no centro da imagem, que ela pode mover e redimensionar com uma pré-visualização redonda.
2. **Given** o quadrado posicionado, **When** a vendedora confirma, **Then** o navegador gera o arquivo quadrado daquele enquadramento (até 512 × 512) e o sistema o guarda como foto de perfil, sem alterar a foto original do álbum.
3. **Given** uma cliente com foto de perfil, **When** aparece na lista de clientes ou no cabeçalho do perfil, **Then** o círculo mostra a foto de perfil no lugar das iniciais.
4. **Given** uma cliente com foto de perfil, **When** a vendedora toca no círculo, **Then** abre a foto original de onde o perfil saiu, inteira e na proporção original.
5. **Given** uma cliente sem foto de perfil, **When** aparece em qualquer lugar, **Then** continua mostrando as iniciais, como hoje.
6. **Given** a foto que é o perfil, **When** ela é excluída do álbum, **Then** a cliente volta a mostrar as iniciais até ter outra foto de perfil.
7. **Given** uma cliente com foto de perfil, **When** a vendedora escolhe outra foto como perfil, **Then** só a nova fica como perfil.
8. **Given** uma foto de perfil definida, **When** a vendedora reabre o enquadramento e move o quadrado, **Then** o navegador gera um recorte novo, o perfil passa a usá-lo, o recorte anterior é descartado se ninguém mais o usa, e a foto original não muda. O editor reabre centrado (o enquadramento anterior não é guardado).
9. **Given** a foto de perfil definida pela conversa ou pela API sem informar recorte, **When** a tela mostra o círculo, **Then** usa o arquivo da própria foto do álbum com recorte central apenas na exibição, e a vendedora é avisada de que ficou sem enquadramento dedicado.
10. **Given** a vendedora posicionando o quadrado, **When** tenta levá-lo para fora da foto ou deixá-lo menor que o tamanho mínimo, **Then** a tela não permite e o mantém dentro dos limites.

---

### User Story 4 - Marcar fotos como favoritas e editar a descrição (Priority: P2)

Ao navegar pelas fotos, a vendedora toca no **coração** para marcar ou desmarcar uma foto como favorita, e pode escrever ou alterar a **descrição** de qualquer foto (por exemplo, "provando o vestido azul, ficou ótimo"). Na grade, as favoritas têm um coração visível.

**Why this priority**: dá ao álbum o comportamento de rede social pedido e ajuda a vendedora a lembrar do que a cliente gostou. Depende de as fotos já existirem (histórias 1 e 2).

**Independent Test**: abrir uma foto, marcar o coração, escrever uma descrição e fechar. Reabrir o perfil e conferir que a foto continua favorita, com o coração na grade, e com a descrição salva. Desmarcar o coração e apagar a descrição, e conferir que as duas mudanças persistem.

**Acceptance Scenarios**:

1. **Given** uma foto ampliada, **When** a vendedora toca no coração, **Then** a foto vira favorita na hora e o coração aparece preenchido nela e na grade.
2. **Given** uma foto favorita, **When** a vendedora toca no coração de novo, **Then** ela deixa de ser favorita.
3. **Given** qualquer foto ampliada, **When** a vendedora edita a descrição e confirma, **Then** a descrição nova é salva e exibida.
4. **Given** uma foto com descrição, **When** a vendedora apaga todo o texto e confirma, **Then** a foto fica sem descrição.
5. **Given** uma descrição com mais caracteres que o máximo permitido, **When** a vendedora tenta salvar, **Then** o sistema avisa o limite e não salva.
6. **Given** a aba Fotos, **When** a vendedora filtra só as favoritas, **Then** a grade mostra apenas as fotos com coração.

---

### User Story 5 - Gerenciar as fotos pela conversa no ChatGPT (MCP) (Priority: P2)

Na conversa com o Ambrosio no ChatGPT, a vendedora manda fotos e diz de quem são. O assistente encontra a cliente, confirma com a vendedora quando o nome é ambíguo, e guarda as fotos no álbum dela, com descrição se a vendedora tiver dado uma. Pela conversa também dá para listar as fotos de uma cliente, marcar e desmarcar favorita, editar a descrição, definir a foto de perfil e excluir fotos.

**Why this priority**: é o canal mais natural para a vendedora, que já conversa com a cliente pelo celular. Depende do álbum (história 1) e reaproveita as regras das histórias 3 e 4.

**Independent Test**: no ChatGPT, mandar duas fotos dizendo "são da Maria Souza, ela provou a saia verde". Conferir que, com duas clientes chamadas Maria Souza, o assistente pergunta qual é, e que as duas fotos aparecem no álbum da escolhida com a descrição. Depois pedir "marca a primeira como favorita e usa como foto de perfil" e conferir o resultado na tela.

**Acceptance Scenarios**:

1. **Given** fotos enviadas na conversa e uma cliente identificada, **When** o assistente guarda as fotos, **Then** elas aparecem no álbum da cliente na tela, na mesma ordem e com as mesmas regras das fotos incluídas pela tela.
2. **Given** um nome que corresponde a mais de uma cliente, **When** a vendedora pede para guardar as fotos, **Then** o assistente mostra as opções e só guarda depois da escolha.
3. **Given** um nome que não corresponde a nenhuma cliente, **When** a vendedora pede para guardar as fotos, **Then** nada é gravado, e o assistente pergunta se deve cadastrar a cliente.
4. **Given** várias fotos numa só mensagem, **When** uma delas é recusada (tipo ou tamanho), **Then** as demais são guardadas, e o assistente informa qual foi recusada e por quê.
5. **Given** uma cliente com fotos, **When** a vendedora pede pela conversa para listar, favoritar, desfavoritar, descrever, definir como perfil ou excluir uma foto, **Then** a mudança aparece igual na tela.
6. **Given** um pedido de exclusão de foto pela conversa, **When** o assistente vai executar, **Then** confirma antes com a vendedora.

---

### User Story 6 - Incluir e gerenciar fotos pela API (Priority: P2)

Uma integração autenticada (o próprio frontend, testes ou outro cliente) consegue, pela API, incluir fotos no álbum de uma cliente, listar as fotos, editar a descrição, marcar e desmarcar favorita, definir e remover a foto de perfil e excluir fotos, com as mesmas regras da tela e da conversa.

**Why this priority**: a API é o caminho comum que a tela e outras integrações usam. As regras de negócio precisam valer igual em todos os canais.

**Independent Test**: com um usuário autenticado, incluir uma foto pela API, listar, favoritar, descrever, definir como perfil e excluir. Conferir o resultado de cada passo na própria API e na tela.

**Acceptance Scenarios**:

1. **Given** um usuário autenticado e uma cliente dele, **When** inclui uma foto válida, **Then** a foto entra no álbum sem descrição (ou com a descrição informada) e não é favorita.
2. **Given** um usuário autenticado, **When** lista as fotos de uma cliente dele, **Then** recebe as fotos da mais recente para a mais antiga, cada uma com um link de leitura temporário, a descrição, se é favorita, se é o perfil e a data de inclusão.
3. **Given** um usuário autenticado, **When** tenta ver, alterar ou excluir uma foto de cliente de outro usuário, **Then** recebe "não encontrado", sem saber se a foto existe.
4. **Given** uma cliente, **When** a consulta da cliente é feita, **Then** traz o link temporário da foto de perfil recortada e o link da foto original correspondente, ou nada se ela não tiver foto de perfil.

---

### Edge Cases

- **Foto de perfil excluída**: se a foto de onde saiu o perfil é excluída, a versão recortada também é removida e a cliente volta para as iniciais.
- **Mesma foto em duas clientes**: a mesma imagem pode estar no álbum de duas clientes diferentes da mesma vendedora (por exemplo, uma foto das duas juntas). Excluir de uma não remove da outra.
- **Foto com dados de localização**: fotos tiradas no celular costumam trazer a localização e outros dados do aparelho. *(Revisto em 2026-10-29: esses dados permanecem no arquivo guardado — ver FR-005. O que não vale mais é a promessa de removê-los.)*
- **Foto girada pelo celular**: uma foto que o celular marcou como girada aparece na orientação correta na grade, ampliada e no recorte do perfil — pelo navegador, que aplica a orientação indicada pelo aparelho ao exibir `<img>` e ao desenhar no `<canvas>`. O servidor não toca nos bytes.
- **Envio interrompido**: se a vendedora fecha a tela ou perde a conexão no meio de um envio, a foto não aparece pela metade. Ou aparece completa, ou não aparece.
- **Link temporário vencido**: se a vendedora deixa o perfil aberto até os links das fotos vencerem, as fotos voltam a carregar ao rolar ou abrir uma delas, sem precisar recarregar a página.
- **Arraste de algo que não é foto**: arrastar um PDF, um texto ou uma pasta para a área de inclusão recusa o item com mensagem clara e não inclui nada.
- **Foto muito grande**: uma foto acima do tamanho máximo é recusada com mensagem que informa o limite.
- **Exclusão da cliente**: se a cliente for excluída, as fotos dela e os arquivos que só ela usava também são removidos.
- **Duas abas abertas**: se a vendedora favorita uma foto numa aba e abre a mesma foto em outra, a segunda mostra o estado atual ao recarregar.

## Requirements *(mandatory)*

### Functional Requirements

#### Álbum de fotos

- **FR-001**: O sistema MUST permitir que cada cliente tenha um álbum com qualquer número de fotos, ligadas à vendedora dona da cliente.
- **FR-002**: Cada foto MUST guardar: a imagem original, uma descrição opcional, a marcação de favorita (sim/não), a origem (tela, API ou conversa no ChatGPT) e a data de inclusão.
- **FR-003**: O sistema MUST aceitar fotos JPEG, PNG e WebP pela **validação de imagem já em vigor na feature 004** (tipo declarado, tamanho máximo e SHA-256), recusando outro tipo com mensagem clara. O sistema MUST NOT identificar o tipo pelo conteúdo do arquivo nem decodificá-lo: decisão do usuário em 2026-10-29 (ver [research.md](research.md)), que mantém a validação da 004 intacta para imagem de produto e item de pedido. Consequência registrada: um arquivo não-imagem declarado como JPEG é aceito e servido, sem renderizar.
- **FR-004**: O sistema MUST guardar a imagem com **os bytes exatamente como chegaram**, sem cortar, redimensionar ou regravar o arquivo que representa a foto original (comportamento da 004).
- **FR-005**: *(revogada em 2026-10-29 pelo usuário)* Remover metadados EXIF/GPS do arquivo exigiria regravar a imagem no servidor, o que pressupõe biblioteca de decodificação — superfície de CVEs e gate de licença que a decisão evitou. Fotos guardadas **mantêm** o metadado que o aparelho gravou. O que continua valendo: a foto de perfil exibida é o recorte gerado no navegador (§FR-012), e um recorte regravado pelo canvas do navegador não carrega EXIF.
- **FR-006**: O sistema MUST evitar fotos repetidas no álbum de uma mesma cliente: incluir de novo uma imagem idêntica a uma já existente no álbum não cria outra foto, e quem enviou é avisado de que a foto já estava lá.
- **FR-007**: A descrição MUST ser opcional, com no máximo 500 caracteres, e MUST poder ser criada, alterada ou apagada a qualquer momento.
- **FR-008**: A marcação de favorita MUST poder ser ligada e desligada a qualquer momento, em qualquer número de fotos da cliente.
- **FR-009**: O sistema MUST permitir excluir uma foto do álbum. Quando a imagem não é usada por nenhuma outra foto, o arquivo também deve ser removido do storage.
- **FR-010**: As fotos MUST ser listadas da mais recente para a mais antiga, em páginas, com opção de mostrar só as favoritas.

#### Foto de perfil

- **FR-011**: O sistema MUST permitir escolher, entre as fotos do álbum, no máximo uma foto de perfil por cliente. Escolher outra substitui a anterior.
- **FR-012**: O quadrado da foto de perfil MUST ser produzido **no navegador** da vendedora, a partir do enquadramento que ela escolher (posição e tamanho do quadrado sobre a foto), e enviado ao sistema como arquivo de até 512 × 512. O sistema MUST NOT decodificar, recortar nem redimensionar imagem no servidor (decisão de 2026-10-29). O sistema MUST NOT detectar nem analisar rostos.
- **FR-013**: Quando a foto de perfil é definida por um canal sem navegador (conversa no ChatGPT ou API sem arquivo de recorte), o sistema MUST usar o próprio arquivo da foto do álbum e o círculo aplica o recorte central apenas na exibição. O enquadramento escolhido **não é persistido**: reabrir o editor recomeça centrado, e o que fica guardado é o arquivo recortado.
- **FR-014**: A foto de perfil MUST manter a ligação com a foto original do álbum, para que quem consulta a cliente receba os links das duas versões e a tela abra a original ao tocar no perfil.
- **FR-015**: O sistema MUST permitir remover a foto de perfil sem excluir a foto do álbum. Excluir a foto de origem MUST remover também a foto de perfil.
- **FR-016**: Na tela, a foto de perfil MUST aparecer redonda em todos os lugares onde hoje aparecem as iniciais da cliente (cabeçalho do perfil, lista de clientes e demais telas que mostram a cliente). Sem foto de perfil, as iniciais continuam.

#### Tela do perfil

- **FR-017**: O perfil da cliente MUST ter as abas **Fotos** e **Pedidos**, com **Fotos** aberta por padrão. A aba Pedidos MUST manter a listagem atual de pedidos.
- **FR-018**: A aba Fotos MUST mostrar as fotos numa grade de quadrados, como num perfil de rede social, preenchendo cada quadrado sem distorcer a imagem (o corte é só visual).
- **FR-019**: Ao tocar numa foto da grade, a tela MUST abri-la ampliada, inteira e na proporção original, com descrição, data, coração de favorita, edição de descrição, opção de usar como perfil, exclusão (com confirmação) e navegação para a foto anterior e a próxima.
- **FR-020**: A aba Fotos MUST ter uma área discreta com o texto "Arraste para incluir uma foto". Fotos soltas nela MUST ser incluídas na hora, sem descrição e sem perguntas adicionais. Tocar ou clicar na área MUST abrir o seletor de arquivos do aparelho.
- **FR-021**: A tela MUST mostrar o progresso de cada foto em envio e o motivo de cada recusa, sem impedir o envio das demais.
- **FR-022**: As favoritas MUST ter um coração visível na grade.

#### Canais e segurança

- **FR-023**: Todas as operações do álbum (incluir, listar, ver, editar descrição, favoritar, definir e remover foto de perfil, excluir) MUST estar disponíveis pela API e pela conversa no ChatGPT (MCP), com as mesmas regras.
- **FR-024**: As fotos MUST ser incluídas apenas pela tela, pela API ou pela conversa no ChatGPT. A importação do +Vendas MUST NOT criar nem alterar fotos de clientes.
- **FR-025**: Uma vendedora MUST NOT ver, alterar nem excluir fotos de clientes de outra vendedora. Tentativas devem receber a mesma resposta de "não encontrado" de uma foto inexistente.
- **FR-026**: As fotos MUST ser lidas apenas por links temporários, como já acontece com as imagens da feature 004. Nenhuma foto pode ficar acessível por um endereço permanente ou público.
- **FR-027**: Na conversa, as ações de escrita MUST exigir a permissão de escrita de imagens já usada pelo plugin, e excluir uma foto MUST pedir confirmação explícita da vendedora.
- **FR-028**: Na conversa, quando o nome da cliente for ambíguo, o assistente MUST mostrar as opções e só gravar depois da escolha. Quando nenhuma cliente for encontrada, MUST NOT gravar nada.
- **FR-029**: O sistema MUST NOT exigir registro de consentimento da cliente para incluir fotos, por nenhum canal. O consentimento é responsabilidade da vendedora e é tratado na política de privacidade do Ambrosio (ver Premissas).

### Key Entities *(include if feature involves data)*

- **Foto da cliente**: uma foto no álbum de uma cliente. Pertence a uma vendedora e a uma cliente, aponta para uma imagem guardada (feature 004) e tem descrição opcional, marcação de favorita, origem e data de inclusão. A mesma imagem guardada pode ser usada por fotos de clientes diferentes da mesma vendedora. **Um arquivo por foto**: não há versão menor nem recorte derivado no servidor.
- **Foto de perfil da cliente**: no máximo uma por cliente. Aponta para a foto do álbum de onde saiu e, quando existirem, para o arquivo quadrado de até 512 × 512 **gerado no navegador** pela vendedora. Sem esse arquivo (canal sem navegador), o círculo usa o arquivo da própria foto.
- **Imagem guardada** (existente, feature 004): o arquivo no storage privado, com tipo, tamanho, dimensões e hash. A foto original e o recorte quadrado são, cada um, uma imagem guardada.
- **Cliente** (existente): ganha o álbum de fotos e, opcionalmente, uma foto de perfil.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A vendedora inclui uma foto no álbum, do arraste até a foto aparecer na grade, em menos de 5 segundos numa conexão comum, sem preencher nenhum campo.
- **SC-002**: A grade de uma cliente com até 100 fotos mostra as primeiras fotos em até 2 segundos ao abrir o perfil.
- **SC-003**: Em 100% das fotos guardadas, o arquivo é **byte-identico** ao recebido (nenhum corte, redimensionamento ou regravação no servidor), e a foto aparece na orientação correta na tela. *(Revisto em 2026-10-29: "nenhum arquivo guardado contém localização" foi retirado com a FR-005.)*
- **SC-004**: A vendedora define uma foto de perfil, da escolha da foto até o círculo atualizado, em menos de 20 segundos, e o círculo mostra exatamente a área que ela enquadrou no navegador.
- **SC-005**: Uma vendedora consegue achar uma cliente na lista pela foto de perfil, sem ler o nome, nos testes com usuárias.
- **SC-006**: 0 casos de uma vendedora acessar foto de cliente de outra vendedora nos testes de isolamento.
- **SC-007**: Toda operação feita pela conversa no ChatGPT aparece na tela ao recarregar o perfil, e vice-versa, com o mesmo resultado.
- **SC-008**: Favoritar ou desfavoritar uma foto reflete na tela em menos de 1 segundo após o toque.

## Assumptions

- **Escopo da HU 010**: esta feature realiza a HU 010 e a amplia com o envio pela tela, as favoritas, as abas e a foto de perfil recortada. As **tags** da HU 010 ficam fora desta entrega: a descrição atende ao pedido.
- **Sem processamento de rosto**: o enquadramento é manual. O sistema não detecta, não identifica e não compara rostos, e não guarda nenhuma característica facial. Assim, a foto continua sendo dado pessoal comum, e não biométrico.
- **Consentimento (LGPD)**: diferente do que a HU 010 previa, não há registro nem bloqueio por consentimento. A vendedora é responsável por ter a autorização da cliente, e a política de privacidade do Ambrosio deve informar que fotos de clientes podem ser guardadas e que passam pela OpenAI quando enviadas pela conversa. A exclusão de fotos (uma a uma, ou todas junto com a cliente) atende a pedidos de remoção da titular.
- **Tamanho e tipos**: valem o tamanho máximo por arquivo e os tipos aceitos (JPEG, PNG, WebP) da feature 004. Não há cota de fotos por cliente nem por vendedora nesta entrega.
- **Exibição ajustada**: as miniaturas da grade são recortes quadrados **só na exibição**. Gerar miniaturas reduzidas para economizar dados é uma otimização que pode ser decidida no planejamento, desde que a foto original continue guardada inteira.
- **Ordem**: a grade segue a data de inclusão, da mais recente para a mais antiga. Não há reordenação manual nem fixação de fotos no topo.
- **Favorita**: é uma marcação da vendedora sobre a foto, sem contagem de curtidas nem outros usuários. Qualquer número de fotos pode ser favorita.
- **Celular**: o arraste vale no computador. No celular (incluindo o app instalado), tocar na área de inclusão abre o seletor de fotos do aparelho.
- **Primeira foto**: incluir fotos não define a foto de perfil automaticamente. A cliente só ganha foto de perfil quando a vendedora escolhe uma, pela tela, API ou conversa.
- **Exclusão**: excluir uma foto é definitivo, sem lixeira. A tela e a conversa pedem confirmação antes.
- **Infraestrutura existente**: storage privado, links temporários e isolamento por vendedora vêm da feature 004. Permissões do plugin, busca de cliente e confirmação de ações vêm das features 005 e 007. O perfil da cliente e a lista de clientes vêm da feature 002.
