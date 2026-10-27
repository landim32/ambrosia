# Feature Specification: Armazenamento de imagens em storage compatível com S3

**Feature Branch**: `004-s3-image-storage`

**Created**: 2026-10-26

**Status**: Draft

**Input**: User description: "docs\historias\003-armazenamento-imagens-s3.md" (HU 003 — infraestrutura única de armazenamento de imagens em storage compatível com S3, com bucket privado, URLs pré-assinadas e metadados no PostgreSQL)

## Contexto

O Ambrosia vai guardar fotos pessoais (fotos de clientes na HU 010 e, no futuro, imagens de produto do bot na HU 009). O modelo de dados já decidiu que o banco guarda **apenas metadados** da imagem (chave, tipo, tamanho, hash, dimensões, dono), nunca o binário, com deduplicação por hash.

Esta feature entrega a **infraestrutura** de armazenamento que essas histórias vão usar: um storage de objetos **compatível com S3**, **privado**, acessado só por **links temporários assinados**, desde o primeiro dia (sem fase de disco local). O provedor provável de produção é o DigitalOcean Spaces, mas o sistema não pode depender dele: trocar de provedor deve exigir apenas configuração. Nos ambientes locais, um storage S3 compatível auto-hospedado (MinIO) faz o papel do provedor.

Não há telas nesta feature: quem a consome são outras partes da API (e, depois, o frontend da HU 004 e o banco de fotos da HU 010). As jornadas abaixo descrevem o uso pela API, pelo cliente que envia arquivos e pelo responsável técnico que opera os ambientes.

## Clarifications

### Session 2026-10-26

- Q: Como a infraestrutura de imagens é exposta na API? → A: Endpoints públicos autenticados de imagem (declarar envio, confirmar, gravar pelo servidor via arquivo enviado à API, consultar e excluir), todos disponíveis em produção.
- Q: Imagens enviadas diretamente pelo navegador com EXIF/GPS: o que fazer? → A: Aceitar como enviadas; o servidor não remove nem recusa EXIF nesse caminho. Remover EXIF antes do envio é responsabilidade das HUs consumidoras com tela.
- Q: Qual storage o ambiente Development usa, dado que Dev roda sem Docker? → A: Reaproveita o MinIO do `docker compose` da homologação (porta exposta no host), com bucket separado `ambrosia-dev` criado pelo mesmo compose.
- Q: Há cota de armazenamento ou de envios por usuário? → A: Não nesta entrega; apenas o limite por arquivo e a limpeza de pendentes.
- Q: O que acontece com imagens que falharam? → A: Ficam como "falhou" e a limpeza periódica as remove após 1 hora; não contam para a deduplicação (novo envio do mesmo hash cria um envio novo).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Enviar uma imagem diretamente ao storage e confirmar (Priority: P1)

Um cliente autenticado (o frontend, um teste ou outra integração) declara que vai enviar uma imagem (tipo, tamanho e hash do conteúdo), recebe um link temporário de envio, envia o arquivo direto ao storage, sem passar pela API, e pede à API a confirmação. A API confere o que chegou e só então disponibiliza a imagem.

**Why this priority**: é o caminho principal de entrada de imagens vindas do navegador, e o que exige todas as garantias de segurança (bucket privado, tipo fixado, conferência posterior). Sem ele nenhuma HU consumidora consegue receber fotos do usuário.

**Independent Test**: com um usuário autenticado, pedir um envio para um JPEG válido, enviar o arquivo pelo link recebido, confirmar e verificar que a imagem fica disponível com um link de leitura que abre o arquivo; repetir com um arquivo adulterado e verificar que a confirmação recusa e o objeto some do storage.

**Acceptance Scenarios**:

1. **Given** um usuário autenticado e uma imagem JPEG, PNG ou WebP dentro do limite de tamanho, **When** declara tipo, tamanho e hash, **Then** recebe o identificador da imagem, um link de envio e o instante em que o link expira (padrão 5 minutos).
2. **Given** um link de envio recebido, **When** o arquivo é enviado com o mesmo tipo declarado, **Then** o storage aceita; **When** é enviado com outro tipo, **Then** o storage recusa (acesso negado).
3. **Given** o arquivo enviado e conferindo com o declarado, **When** o cliente pede a confirmação, **Then** a imagem passa a "disponível" e a resposta traz um link de leitura temporário e o instante de expiração.
4. **Given** um arquivo enviado maior que o limite configurado, com tipo real fora da lista aceita ou com hash diferente do declarado, **When** o cliente pede a confirmação, **Then** a API recusa com mensagem clara, apaga o objeto do storage e marca a imagem como "falhou".
5. **Given** um arquivo com extensão e tipo declarados de imagem, mas cujo conteúdo real é outro formato (por exemplo, um PDF renomeado para `.jpg`), **When** é confirmado, **Then** a API identifica o tipo pelo conteúdo, recusa e apaga o objeto.
6. **Given** um pedido de envio com tipo fora da lista aceita ou tamanho declarado acima do limite, **When** é feito, **Then** a API recusa na hora, sem gerar link de envio.
7. **Given** um usuário que já tem uma imagem disponível com o mesmo hash, **When** declara um novo envio desse mesmo conteúdo, **Then** a API devolve a imagem existente (indicando que ela já existe), sem link de envio e sem criar outro objeto ou outro metadado.
8. **Given** um pedido de confirmação de uma imagem cujo arquivo nunca foi enviado, **When** é feito, **Then** a API recusa com mensagem clara e a imagem permanece pendente até expirar.

---

### User Story 2 - Ler uma imagem só por link temporário e só pelo dono (Priority: P1)

O dono de uma imagem disponível obtém um link de leitura temporário. Ninguém consegue abrir o arquivo sem esse link, o link para de funcionar no prazo configurado e um usuário nunca obtém link de imagem de outro usuário.

**Why this priority**: são fotos pessoais. Bucket privado, links curtos e isolamento por usuário são o requisito de privacidade central da HU; sem eles a infraestrutura não pode ser usada pela HU 010.

**Independent Test**: com as usuárias Ana e Bia, Ana envia uma imagem; Ana obtém o link e abre o arquivo; o endereço do objeto sem assinatura é recusado pelo storage; o link de Ana deixa de funcionar após o prazo; Bia pede a mesma imagem e recebe "não encontrado".

**Acceptance Scenarios**:

1. **Given** uma imagem disponível de Ana, **When** Ana pede a imagem, **Then** recebe os metadados (tipo, tamanho, dimensões quando conhecidas) e um link de leitura válido por 15 minutos (padrão configurável), com o instante de expiração.
2. **Given** um objeto recém-gravado no storage, **When** alguém acessa o endereço do objeto sem assinatura, **Then** o storage recusa (acesso negado).
3. **Given** um link de leitura emitido, **When** é usado depois do prazo de validade, **Then** o storage recusa.
4. **Given** uma imagem de Ana, **When** Bia pede essa imagem (ou um link para ela), **Then** recebe "não encontrado", indistinguível de uma imagem inexistente.
5. **Given** uma imagem pendente ou que falhou, **When** o dono pede a imagem, **Then** não recebe link de leitura (a imagem não está disponível).
6. **Given** qualquer resposta da API que contenha uma imagem, **When** é gerada, **Then** o link de leitura é gerado naquele momento e nunca é lido do banco (o banco não guarda links).

---

### User Story 3 - Gravar imagens a partir do próprio servidor (Priority: P2)

Quando é a própria API que obtém o arquivo (por exemplo, uma foto baixada de uma integração, como na HU 010), ela grava a imagem no storage diretamente, aplicando as mesmas validações de tipo, tamanho e deduplicação e removendo metadados de localização (EXIF/GPS) antes de gravar.

**Why this priority**: é o segundo caminho de entrada, necessário para a HU 010, mas não bloqueia o envio pelo navegador nem a leitura.

**Independent Test**: enviar à API uma foto JPEG com coordenadas GPS no EXIF; verificar que a imagem fica disponível, que o arquivo lido pelo link não contém mais os dados de localização e que reenviar o mesmo arquivo devolve a mesma imagem.

**Acceptance Scenarios**:

1. **Given** um usuário autenticado e um arquivo de imagem válido recebido pela API, **When** a API o grava, **Then** a imagem fica disponível imediatamente, com tipo, tamanho, hash e dimensões registrados, e a resposta traz um link de leitura temporário.
2. **Given** uma foto com dados EXIF de localização, **When** é gravada pelo servidor, **Then** o arquivo armazenado não contém esses dados.
3. **Given** um arquivo com tipo real fora da lista ou acima do limite, **When** a API tenta gravá-lo, **Then** recusa com mensagem clara e nada é gravado no storage nem no banco.
4. **Given** o mesmo conteúdo gravado duas vezes pelo mesmo usuário, **When** a segunda gravação ocorre, **Then** a API devolve a imagem existente, sem novo objeto nem novo metadado.

---

### User Story 4 - Excluir uma imagem definitivamente (Priority: P2)

O dono de uma imagem pode excluí-la. A exclusão remove o arquivo do storage e o metadado do banco, sem deixar cópia acessível. A mesma capacidade permite apagar de uma vez todas as imagens de um usuário, base para o direito de exclusão da LGPD.

**Why this priority**: obrigação de privacidade (LGPD) e necessária para limpar envios recusados; não bloqueia o fluxo de envio e leitura.

**Independent Test**: Ana exclui uma imagem disponível; a consulta da imagem passa a "não encontrado", o objeto não existe mais no storage e Bia não consegue excluir imagens de Ana.

**Acceptance Scenarios**:

1. **Given** uma imagem de Ana, **When** Ana a exclui, **Then** o objeto deixa de existir no storage e o metadado deixa de existir no banco; consultas posteriores respondem "não encontrado".
2. **Given** uma imagem de Ana, **When** Bia tenta excluí-la, **Then** recebe "não encontrado" e a imagem continua intacta.
3. **Given** um usuário com várias imagens, **When** a exclusão de todas as imagens do usuário é acionada, **Then** todos os objetos daquele usuário e todos os metadados correspondentes são removidos, sem afetar imagens de outros usuários.
4. **Given** uma imagem excluída, **When** o mesmo conteúdo é enviado de novo pelo mesmo usuário, **Then** é tratado como envio novo (não há deduplicação contra imagens excluídas).

---

### User Story 5 - Operar os ambientes só com configuração (Priority: P2)

O responsável técnico configura o storage (endereço do serviço, região, bucket, estilo de endereçamento, credenciais, prazos, limite de tamanho e tipos aceitos) por arquivo de configuração e variáveis de ambiente. A API recusa subir com configuração incompleta ou inválida, os segredos nunca vazam, e a homologação local sobe o storage pronto (bucket criado, privado e com as regras de acesso do navegador aplicadas). Trocar o storage local pelo provedor de produção exige só mudar a configuração.

**Why this priority**: sem isso não há ambiente para testar nem para publicar; é pré-requisito operacional, mas o valor visível ao usuário está nas US1 a US4.

**Independent Test**: subir a API com o bucket vazio na configuração e confirmar que ela não inicia, com mensagem que nomeia a chave faltante sem mostrar segredos; subir a homologação do zero e confirmar que o bucket existe, é privado e aceita envio a partir da origem do frontend; rodar a mesma suíte de testes de API contra o storage local e contra um bucket do provedor de produção só trocando a configuração.

**Acceptance Scenarios**:

1. **Given** a configuração sem endereço do serviço, bucket, chave de acesso ou chave secreta, **When** a API inicia, **Then** ela não sobe e informa qual configuração falta, sem exibir valores secretos.
2. **Given** um prazo de leitura fora de 1 a 60 minutos ou de envio fora de 1 a 15 minutos (ou limite de tamanho não positivo, ou lista de tipos vazia), **When** a API inicia, **Then** ela não sobe e informa a faixa permitida.
3. **Given** a homologação reconstruída do zero, **When** sobe, **Then** o storage local está no ar com o bucket criado, sem acesso público, e com regras que permitem leitura e envio pelo navegador apenas a partir da origem do frontend.
4. **Given** a mesma versão da API, **When** a configuração aponta para o storage local ou para o provedor de produção, **Then** a mesma suíte de testes de API passa nos dois, sem mudança de código.
5. **Given** qualquer erro de storage, registro em log, resposta de erro ou documentação interativa, **When** é inspecionado, **Then** não contém chave de acesso, chave secreta nem links assinados completos.

---

### User Story 6 - Limpar envios abandonados (Priority: P3)

Envios declarados e nunca confirmados, e envios recusados, não ficam para sempre: periodicamente o sistema apaga do storage e do banco as imagens que continuam pendentes, ou que falharam, há mais de 1 hora.

**Why this priority**: evita lixo e custo no storage e fecha a brecha de alguém subir arquivos sem confirmar, mas não impede o uso das demais histórias.

**Independent Test**: declarar um envio, enviar o arquivo e não confirmar; após o prazo de abandono e a execução da limpeza, verificar que o objeto e o metadado não existem mais.

**Acceptance Scenarios**:

1. **Given** uma imagem pendente há mais de 1 hora (com ou sem arquivo enviado), **When** a limpeza roda, **Then** o objeto (se existir) e o metadado são removidos.
2. **Given** uma imagem pendente há menos de 1 hora, **When** a limpeza roda, **Then** ela permanece e ainda pode ser confirmada.
3. **Given** imagens disponíveis, **When** a limpeza roda, **Then** nenhuma delas é afetada.
4. **Given** uma imagem que falhou há mais de 1 hora, **When** a limpeza roda, **Then** o metadado é removido.

---

### Edge Cases

- **Envio maior que o declarado**: o link de envio não limita o tamanho do corpo; a confirmação confere o tamanho real e recusa/apaga o que passar do limite ou divergir do declarado (US1 cenário 4), e a limpeza remove o que nunca for confirmado (US6).
- **Confirmação repetida** de uma imagem já disponível: responde com a imagem disponível e um link novo, sem reprocessar nem duplicar.
- **Confirmação de imagem que falhou**: recusada; o cliente precisa declarar um novo envio, que é aceito mesmo com o mesmo hash (imagens que falharam não contam para a deduplicação) e gera um novo identificador.
- **Confirmação depois do link de envio expirado**, mas com o arquivo já enviado dentro do prazo e a imagem ainda pendente: aceita, desde que o arquivo confira e a limpeza ainda não tenha rodado.
- **Dois pedidos de envio simultâneos do mesmo conteúdo pelo mesmo usuário**: resultam em uma única imagem; o segundo recebe a imagem existente.
- **Mesmo conteúdo enviado por usuários diferentes**: gera uma imagem e um objeto para cada usuário (não há deduplicação entre contas).
- **Pedido de envio com o hash de uma imagem pendente do próprio usuário**: devolve a mesma imagem pendente com um novo link de envio, em vez de criar outra.
- **Arquivo vazio, hash mal formado ou tipo declarado diferente do tipo real**: recusados com mensagem clara.
- **Imagem cujo cabeçalho não permite ler dimensões**: é aceita se o tipo for válido; largura e altura ficam sem valor.
- **Storage fora do ar**: as operações que dependem dele respondem erro genérico de indisponibilidade, sem expor endereço, credenciais ou detalhes internos; nada fica marcado como disponível sem o objeto existir.
- **Exclusão de imagem cujo objeto já não existe no storage**: o metadado é removido mesmo assim e a exclusão é considerada bem-sucedida.
- **Usuário sem autenticação**: todas as operações de imagem respondem "não autorizado".
- **Foto com EXIF/GPS**: aceita e guardada como enviada, sem remoção nem recusa, nos dois caminhos (FR-010 retirado — ver decisions.md, "remoção do ImageSharp").
- **Tipos fora da lista** (HEIC, GIF, SVG etc.): recusados nesta entrega; a lista é configurável.

## Requirements *(mandatory)*

### Functional Requirements

**Armazenamento e independência de provedor**

- **FR-001**: O sistema DEVE armazenar o conteúdo das imagens exclusivamente em um storage de objetos compatível com S3; o banco DEVE guardar apenas metadados (chave do objeto, tipo, tamanho, hash, dimensões, situação, origem, dono e datas), nunca o binário.
- **FR-002**: O sistema DEVE funcionar com qualquer storage compatível com S3 configurável por endereço do serviço, região, bucket, estilo de endereçamento (por caminho ou por subdomínio) e credenciais, sem depender de recursos exclusivos de um provedor. Trocar o storage local pelo provedor de produção DEVE exigir apenas configuração.
- **FR-003**: A chave de cada objeto DEVE seguir o formato `ambrosio/users/{userId}/images/{sha256}.{ext}`, agrupando os objetos por usuário.
- **FR-004**: O bucket DEVE ser privado: nenhum objeto pode ser legível sem assinatura, e o sistema NUNCA DEVE gravar objetos com permissão pública.

**Envio pelo cliente (US1)**

- **FR-005**: O sistema DEVE permitir a um usuário autenticado declarar um envio informando tipo, tamanho e hash SHA-256 do conteúdo, recusando na hora tipo fora da lista aceita, tamanho não positivo ou acima do limite e hash mal formado.
- **FR-006**: Para um envio novo, o sistema DEVE registrar a imagem como "pendente" e devolver o identificador, um link de envio temporário que só aceita o tipo declarado e o instante de expiração do link (padrão 5 minutos, faixa de 1 a 15).
- **FR-007**: Na confirmação, o sistema DEVE conferir no storage que o objeto existe, que o tamanho real é igual ao declarado e não excede o limite, que o tipo real identificado pelos primeiros bytes do conteúdo (*magic bytes*) está na lista aceita e coincide com o declarado e que o hash do conteúdo é igual ao declarado.
- **FR-008**: Se a conferência passar, a imagem DEVE ficar "disponível", com dimensões registradas quando for possível lê-las, e a resposta DEVE trazer um link de leitura. Se falhar, o sistema DEVE apagar o objeto, marcar a imagem como "falhou" e responder com mensagem que indique o motivo (tamanho, tipo ou hash), sem detalhes internos.

**Envio pelo servidor (US3)**

- **FR-009**: O sistema DEVE oferecer a gravação de uma imagem a partir de um arquivo que a própria API recebe ou obtém, validando tipo pelo conteúdo, tamanho e hash antes de gravar e registrando a origem (envio do cliente, servidor, ChatGPT ou bot).
- **FR-010**: ~~Na gravação pelo servidor, o sistema DEVE remover os metadados EXIF antes de gravar.~~ **Retirado** (decisions.md, "remoção do ImageSharp"). O hash usado na chave e na deduplicação continua sendo o do conteúdo efetivamente armazenado.

**Deduplicação**

- **FR-011**: O sistema DEVE garantir no máximo uma imagem pendente ou disponível (e um objeto) por par usuário + hash; imagens que falharam não contam para a deduplicação, e um novo envio do mesmo conteúdo cria um envio novo. Um novo envio ou gravação de conteúdo já disponível para o mesmo usuário DEVE devolver a imagem existente, indicando que ela já existia. Não DEVE haver deduplicação entre usuários diferentes.

**Leitura e isolamento (US2)**

- **FR-012**: O sistema DEVE permitir ao dono consultar uma imagem disponível, devolvendo metadados e um link de leitura temporário com o instante de expiração (padrão 15 minutos, faixa de 1 a 60).
- **FR-013**: Links de leitura e de envio DEVEM ser gerados a cada resposta e NUNCA DEVEM ser gravados no banco nem registrados por completo em log.
- **FR-014**: Toda consulta, confirmação e exclusão de imagem DEVE filtrar pelo usuário autenticado; uma imagem de outro usuário DEVE responder "não encontrado", indistinguível de imagem inexistente.
- **FR-015**: Imagens pendentes ou que falharam NÃO DEVEM gerar link de leitura.

**Exclusão (US4)**

- **FR-016**: O dono DEVE poder excluir uma imagem; a exclusão DEVE remover o objeto do storage e o metadado do banco, e o objeto não DEVE mais existir no storage após a resposta.
- **FR-017**: O sistema DEVE oferecer, para uso interno (por exemplo, pelo fluxo de exclusão de conta), a remoção de todas as imagens de um usuário (objetos sob o prefixo do usuário e metadados), sem afetar outros usuários.

**Limpeza (US6)**

- **FR-018**: O sistema DEVE executar periodicamente a remoção de imagens que estejam pendentes ou que falharam há mais de 1 hora, apagando o objeto (se existir) e o metadado, sem tocar em imagens disponíveis.

**Configuração e operação (US5)**

- **FR-019**: Endereço do serviço, região, bucket, estilo de endereçamento, chave de acesso, chave secreta, prazo de leitura, prazo de envio, limite de tamanho (padrão 10 MB) e lista de tipos aceitos (padrão JPEG, PNG e WebP) DEVEM ser configuráveis por arquivo de configuração e por variáveis de ambiente, com segredos fora do controle de versão.
- **FR-020**: A API NÃO DEVE iniciar se endereço do serviço, bucket, chave de acesso ou chave secreta estiverem vazios, se os prazos estiverem fora das faixas permitidas, se o limite de tamanho não for positivo ou se a lista de tipos estiver vazia; a mensagem DEVE nomear a configuração inválida sem exibir valores secretos.
- **FR-021**: Credenciais do storage e links assinados completos NUNCA DEVEM aparecer em log, resposta de erro ou documentação interativa da API.
- **FR-022**: A homologação local DEVE subir um storage S3 compatível com dois buckets já criados e privados — o da homologação e o `ambrosia-dev`, usado pelo ambiente Development (que roda sem Docker e acessa esse storage pela porta exposta no host) — cada um com regras de acesso pelo navegador (leitura e envio) liberadas apenas para a origem do frontend do respectivo ambiente; a produção DEVE liberar apenas a origem `https://ambrosiaai.site`.
- **FR-023**: A política de segurança de conteúdo do frontend DEVE permitir carregar imagens e enviar arquivos para o endereço do storage configurado no ambiente, e só para ele.

**Exposição na API**

- **FR-025**: A API DEVE expor, em todos os ambientes (inclusive produção) e apenas para usuários autenticados, as operações de imagem: declarar envio, confirmar envio, gravar pelo servidor (arquivo enviado à própria API, sujeito ao mesmo limite de tamanho), consultar e excluir. Essas operações DEVEM aparecer na documentação interativa da API.

**Testes de API**

- **FR-024**: A suíte de testes de API DEVE cobrir: envio + confirmação com sucesso; tipo diferente no envio recusado pelo storage; confirmação recusada por tamanho, tipo real e hash, com o objeto apagado; arquivo disfarçado (PDF como JPEG); deduplicação; acesso sem assinatura recusado; expiração do link de leitura; isolamento entre os usuários A e B (consulta e exclusão); exclusão removendo o objeto; gravação pelo servidor com remoção de EXIF; e acesso sem autenticação.

### Key Entities

- **Imagem armazenada**: registro dos metadados de um arquivo de imagem guardado no storage. Atributos: identificador, dono (usuário), chave do objeto (única), hash SHA-256 (único por dono entre imagens pendentes e disponíveis), tipo, tamanho em bytes, largura e altura (opcionais), situação (pendente, disponível, falhou), origem (envio do cliente, servidor, ChatGPT, bot) e datas de criação e atualização. Pertence a um usuário; um usuário possui várias imagens.
- **Usuário**: dono das imagens (já existente, HU 001). A exclusão de todas as imagens de um usuário é feita pelo prefixo dele no storage e pelo dono no banco.
- **Link de envio**: autorização temporária, emitida para uma imagem pendente, que permite gravar um único objeto com o tipo declarado. Não é persistido.
- **Link de leitura**: autorização temporária para ler o objeto de uma imagem disponível. Não é persistido.
- **Configuração do storage**: endereço, região, bucket, estilo de endereçamento, credenciais, prazos, limite de tamanho e tipos aceitos, por ambiente.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A mesma suíte de testes de API passa 100% contra o storage local e contra um bucket do provedor de produção, trocando apenas a configuração (0 linhas de código alteradas).
- **SC-002**: 100% das tentativas de abrir um objeto sem assinatura são recusadas pelo storage.
- **SC-003**: 100% dos links de leitura deixam de funcionar após o prazo configurado (padrão 15 minutos), e 100% dos links de envio após o seu prazo (padrão 5 minutos).
- **SC-004**: 100% dos envios adulterados (tamanho acima do limite ou divergente, tipo real fora da lista ou diferente do declarado, hash divergente) são recusados na confirmação e o objeto correspondente deixa de existir no storage.
- **SC-005**: Enviar o mesmo arquivo duas vezes pelo mesmo usuário resulta em exatamente 1 objeto e 1 registro de imagem.
- **SC-006**: 0 casos em que um usuário obtém metadados, link de leitura ou exclusão de imagem de outro usuário (todos respondem "não encontrado").
- **SC-007**: Após a exclusão, a verificação de existência do objeto no storage responde "não encontrado" em 100% dos casos.
- **SC-008**: A API se recusa a iniciar em 100% das combinações de configuração obrigatória ausente ou fora da faixa testadas, e 0 ocorrências de chave de acesso, chave secreta ou link assinado completo em logs, respostas de erro e documentação interativa.
- **SC-009**: A homologação reconstruída do zero tem o storage pronto (bucket privado criado e regras de acesso do navegador aplicadas) sem nenhum passo manual.
- **SC-010**: Um envio de uma imagem de até 10 MB, do pedido à confirmação, é concluído em menos de 10 segundos numa conexão de banda larga comum.
- **SC-011**: 0 imagens pendentes ou que falharam com mais de 2 horas no banco ou no storage após a limpeza estar ativa (prazo de abandono de 1 hora mais o intervalo de execução).

## Assumptions

- **Escopo de infraestrutura**: não há telas de envio nem de galeria nesta feature (a HU 002 só exibe). Vínculo de imagens a clientes, foto principal e tags ficam para a HU 010; cópia das imagens de produto do +Vendas fica para uma HU futura.
- **Endpoints próprios de imagem** (Clarifications 2026-10-26): ver FR-025. A exclusão de todas as imagens de um usuário é só interna. As HUs consumidoras podem devolver o link de leitura dentro dos seus próprios dados.
- **Sem proxy de leitura nem CDN**: as imagens são lidas direto do endereço do provedor pelo link assinado; domínio próprio e CDN ficam fora desta HU (decidido em 25/10/2026).
- **Provedor e região**: produção usa um provedor S3 compatível (provavelmente DigitalOcean Spaces); região e eventual exigência de manter dados no Brasil são decisões de configuração e contratação, sem impacto no comportamento especificado.
- **Hash no envio pelo cliente**: a conferência do hash é feita pela API na confirmação. Se o provedor também validar o hash no próprio envio, isso é um reforço opcional, não um requisito.
- **EXIF** (Clarifications 2026-10-26): removido somente quando o servidor processa o arquivo (US3). Arquivos enviados diretamente pelo cliente ao storage são guardados como enviados, sem recusa por EXIF; remover EXIF no próprio navegador antes do envio é responsabilidade das HUs consumidoras com tela (ex.: 010).
- **Miniaturas e HEIC**: miniaturas/redimensionamento e aceitação de HEIC (com conversão) ficam fora desta entrega; a lista de tipos é configurável.
- **Ambientes (constituição §VII; Clarifications 2026-10-26)**: Development roda sem Docker e aponta para o MinIO da homologação exposto no host (bucket `ambrosia-dev`), com valores de dev não secretos; usar Development exige a homologação (ou ao menos o serviço de storage dela) no ar. Homolog sobe o MinIO no `docker compose` do WSL, com bucket e regras criados automaticamente; Production aponta para o provedor contratado, sem MinIO.
- **Credencial de produção**: a chave de acesso de produção tem permissão restrita ao bucket do Ambrosia (a confirmar no painel do provedor).
- **Backups do bucket**: política de retenção de backups e propagação da exclusão LGPD para eles ficam fora desta HU.
- **Sem cota por usuário** (Clarifications 2026-10-26): não há limite de espaço total, de quantidade de imagens nem de envios por período por usuário nesta entrega; o controle de custo/abuso se limita ao limite por arquivo (FR-019) e à limpeza de pendentes (FR-018). Cotas e rate limiting ficam para uma feature futura.
- **Limpeza**: roda dentro da própria API em intervalo regular (por exemplo, a cada 15 minutos), sem infraestrutura adicional de agendamento.
- **Testes**: backend coberto apenas por testes de API contra a homologação (constituição §VII e §VIII); testes de expiração usam prazos curtos configurados no ambiente de teste.
