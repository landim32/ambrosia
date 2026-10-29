# Research: Fotos de clientes no estilo rede social

**Feature**: `009-customer-photos` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

> ## Revisão de escopo — 2026-10-29 (antes da implementação)
>
> O plano original punha uma biblioteca de decodificação de imagem no servidor
> (`SixLabors.ImageSharp`) para normalizar o arquivo, remover EXIF/GPS, gerar miniatura 256 e recortar o
> quadrado do perfil. **Essa decisão foi revista e revertida pelo usuário**, depois de a verificação
> abaixo:
>
> 1. `SixLabors.ImageSharp` 3.1.12 (a última da linha 3.1, que o plano fixou) carrega **5 advisories** no
>    feed de auditoria do NuGet: `GHSA-v76p-62qx-wwq2`, `GHSA-j3p4-wp97-rph4`, `GHSA-j9gm-c75j-xc9q` e
>    `GHSA-jjfr-hcj7-qf5w` (altas) e `GHSA-wmxv-xphr-5c9g` e `GHSA-gwg2-r3hj-4w44` (médias). Todos os
>    cinco estão no **codec TIFF** (4) ou no **parse de perfil ICC embutido** (1). Corrigidos só na 3.2.0.
> 2. A 3.2.0 em diante **quebra o build em Release** sem licença paga: medido nesta máquina com um
>    projeto descartável — `error : No Six Labors license found. Set $(SixLaborsLicenseKey)…`, vindo de
>    `build/SixLabors.ImageSharp.targets`. O `backend/Dockerfile` publica `-c Release` nos dois hosts e no
>    bundle do migrator, e o `deploy-prod.yml` chama esse Dockerfile. O texto do `LICENSE` é idêntico em
>    3.1.12, 3.2.0 e 4.1.3 (Split License: Apache 2.0 abaixo de US$ 1 mi de receita anual) — a diferença
>    é o *gate de build*, não a licença.
> 3. `SkiaSharp` 4.154 (MIT, sem gate) foi testado contra os fixtures reais do repositório: decodifica
>    JPEG/PNG/WebP e codifica WebP (recorte 512 → 10 686 bytes), mas **decodifica GIF também** (a recusa
>    de tipo não viria "de graça"), **lança exceção** em vez de devolver nulo para bytes inválidos, não
>    aplica nem expõe a orientação EXIF, não informa o formato realmente decodificado, e exigiria
>    `SkiaSharp.NativeAssets.Linux` dentro da imagem Docker — o nativo que a R1 original evitou
>    explicitamente porque a imagem já carrega Chromium e lotou o disco no deploy da 008.
> 4. Fato decisivo encontrado no código da própria feature 004:
>    `ImageService.ConfirmUploadAsync` já grava com `width: null, height: null` e o comentário
>    *"Sem processador de imagem, as dimensões ficam desconhecidas."* Ou seja, **o storage de imagens do
>    Ambrosio hoje não decodifica nada**, e nada nesta feature depende de decodificar.
>
> **Decisão do usuário**: nenhuma biblioteca de imagem no servidor. A lista de fotos serve o **original**;
> o quadrado da foto de perfil é gerado **no navegador**; o EXIF dos arquivos permanece como está
> (aceito). A validação da 004 (mime declarado + tamanho + SHA-256) fica **intacta** — a checagem de
> assinatura real foi oferecida e recusada, para não alterar comportamento de outras histórias.
>
> O que isso retira da spec: FR-005 (remover EXIF/GPS), a miniatura de grade, o limite de 50 megapixels e
> o recorte quadrado no servidor. O que ela ganha: nenhum pacote novo no backend, e a paridade
> ferramenta↔rota sem exceções. As FRs afetadas foram reescritas em [spec.md](spec.md); nada aqui é
> mudança silenciosa.

## Ponto de partida (o que já existe)

| Peça | Onde | O que serve para esta feature |
|---|---|---|
| `stored_images` + `IImageService` | feature 004 | Storage privado, URL pré-assinada, dedup por `(user_id, sha256)`, `StoreFromServerAsync` (gravação pelo servidor), `GetByIdAsync` (URL nova sob demanda) e exclusão do objeto |
| `POST /images` multipart | `ImagesController` | Rota que já recebe bytes do navegador e devolve `imageId` — é por aqui que entram as fotos da tela e o recorte do perfil |
| Fluxo pré-assinado (`POST /images/uploads` → `PUT` → confirmar) | feature 004 | Alternativa direta ao multipart; **não** usada por esta feature (ver R7) |
| `GET /images/{imageId}` | feature 004 | Renovação de URL de **qualquer** imagem, inclusive foto de cliente — substitui a rota de renovação por foto (R10) |
| `SafeFileDownloader` + `FileArgument` | host do MCP | Download seguro de `download_url` (só https, sem IP privado, limite de bytes) já usado por `enviar_imagem` |
| `WriteToolHelper` (`confirmar`), `[AmbrosioScopes]`, `[AmbrosioEndpoint]`, `ParityTests` | host do MCP | Padrão das tools de escrita e da paridade tool ↔ rota REST |
| `ImageSource` (`Upload`/`Server`/`ChatGpt`/`Bot`) | feature 004 | Origem do binário; esta feature usa `Upload`/`Server`/`ChatGpt` sem criar enum novo |
| `CustomerAvatar` com `photoUrl` reservado | frontend | Avatar redondo com fallback de iniciais (Radix Avatar), já usado em lista, perfil e pedido |
| `Modal`, `ConfirmModal`, `Skeleton`, `sonner` | frontend (`components/…`) | Visualizador ampliado, editor de recorte, confirmação de exclusão e aviso |
| `ImageService` (frontend) + `useStoredImageUrl` | frontend | Cache de URL por `imageId` até perto de `urlExpiresAt`, com prometidas compartilhadas — o mesmo padrão serve às fotos |

O que **não** existe e esta feature também **não** cria: biblioteca de processamento de imagem no backend
(decisão do usuário, cabeçalho desta página), detecção de tipo pelo conteúdo do arquivo, miniatura
gerada no servidor e exclusão de cliente (não há `DELETE /customers/{id}`).

---

## R1 — Processamento de imagem no servidor

**Decision**: **nenhum**. Nenhum pacote novo em `Ambrosio.Infra`, `Ambrosio.API` ou `Ambrosio.ApiTests`. O
binário da foto é gravado exatamente como chegou, pelo caminho que a feature 004 já usa.

**Rationale**: ver o bloco de revisão no topo — custo de licença, superfície de CVEs e o fato de a 004 já
operar sem decodificador. As três coisas que um decodificador serviriam (remover EXIF, limitar
megapixels, gerar recorte/miniatura) deixaram de ser requisito do usuário. Manter o servidor sem codec
também mantém a superfície de ataque de `POST /images` exatamente como foi auditada na 004: um arquivo
malformado nunca é decodificado por processo algum do Ambrosio.

**Alternatives considered**:
- *ImageSharp 3.1.12 com `Configuration` restrita a JPEG/PNG/WebP + `DecoderOptions.SkipMetadata`*: **medido
  nesta máquina** — `new Configuration()` nasce com zero formatos; registrando só os três módulos
  (`JpegConfigurationModule`, `PngConfigurationModule`, `WebpConfigurationModule`), o GIF e um TIFF
  disfarçado de `.jpg` são recusados com `UnknownImageFormatException` sem tocar no decoder TIFF, enquanto
  `Configuration.Default` (que traz `TIFF` registrado) chega a instanciá-lo. Fecharia os 5 advisories por
  alcançabilidade e o pipeline completo (AutoOrient + crop 512 + `SaveAsWebp` → 10 440 bytes) roda na
  config restrita. Recusada pelo usuário ("não use o ImageSharp").
- *ImageSharp 3.2.0 ou 4.1.3 (corrigidos)*: exigem `sixlabors.lic`/`SixLaborsLicenseKey` no build Release,
  ou seja, licença paga e chave no repositório/CI/Docker. Recusada.
- *SkiaSharp*: testado e descartado — nativo na imagem, sem orientação EXIF, sem formato real decodificado
  (detalhes no bloco de revisão).
- *Cortar dentro do Chromium do coletor do +Vendas*: descartado porque a FR-024 não permite que a
  importação crie fotos de cliente — o caminho simplesmente não existe.

## R2 — O que é gravado

**Decision**: para cada foto, o Ambrosio guarda **o arquivo que chegou**, sem regravar:

1. Bytes pelo `POST /images` (tela) ou por `IImageService.StoreFromServerAsync` (conversa), ambos da 004.
2. Validação da 004, sem alteração: mime declarado ∈ `ObjectStorage:AllowedMimeTypes`, tamanho ≤
   `MaxUploadBytes` (10 MB), `SHA-256` conferido na confirmação, dedup por `(user_id, sha256)`.
3. `stored_images.width`/`height` seguem **nulos** (a 004 não decodifica). Nada nesta feature depende deles.
4. Metadado EXIF/GPS permanece no arquivo (FR-005 reescrita; decisão do usuário).

**Rationale**: é o comportamento já auditado da 004. A consequence a registrar é que **um arquivo cujo
`Content-Type` declarado seja um tipo aceito, mas cujo conteúdo não seja imagem, é aceito e servido** — o
navegador não renderiza, nada executa. Aceito explicitamente pelo usuário para não mudar a validação da
004, que serve imagem de produto e item de pedido.

**Orientação de foto girada pelo celular**: resolvida pelo navegador, não pelo servidor. Chrome, Firefox e
Safari aplicam `image-orientation: from-image` em `<img>` desde 2017, então grade e visualizador mostram a
foto em pé sem trabalho nosso. O editor de recorte também: `react-easy-crop` desenha um `<img>` e o
`canvas` recebe o bitmap já orientado, e por isso o recorte gravado sai na posição correta — ver R4.

## R3 — Grade sem miniatura

**Decision**: a grade usa **o original**, com recorte quadrado só no CSS (`aspect-square` + `object-cover`),
e carregamento por página de 24 com rolagem infinita. Não existe coluna de miniatura nem segundo arquivo
por foto.

**Rationale**: decisão do usuário ("servir o original, sem recorte/miniatura"). O custo honesto é de
tráfego: uma página de 24 fotos pode levar 24 arquivos de até 10 MB, o que ameaça o SC-002 (primeiras
fotos em 2 s) e o conforto em dados móveis. Mitigações que não exigem processamento no servidor, já
embutidas no desenho: página de 24, `loading="lazy"` na grade, e `src` só quando a sentinela de
`IntersectionObserver` chega perto. **Risco registrado**: se a grade ficar lenta em uso real, a saída é
redimensionar **no navegador antes do envio** (canvas ≤ 1600 px no lado maior) — o que mudaria a FR-004 ao
reduzir resolução e, de brinde, removeria EXIF das fotos da tela. É decisão de produto futura, não
aceno aqui.

## R4 — Foto de perfil: recorte feito no navegador

**Decision**:
- O quadrado do perfil é produzido **no navegador** da vendedora: `react-easy-crop` (`aspect={1}`,
  `cropShape="round"`) sobre um `<canvas>` que desenha a área escolhida e gera um arquivo WebP/JPEG de até
  **512 × 512**. Esse arquivo sobe por `POST /images` e vira o `imageId` do perfil.
- A API recebe o recorte pronto: `PUT /customers/{customerId}/profile-photo` com `{ photoId, imageId? }`.
  `imageId` é opcional — sem ele (caso do ChatGPT, que não tem navegador), o círculo usa a própria foto do
  álbum recortada pelo CSS.
- Nenhum enquadramento é persistido: para reenquadrar, a vendedora reabre o editor sobre a foto original
  do álbum, e o recorte anterior é apagado se ficou órfão (R6).
- O recorte **não** entra na lista de fotos: a foto original continua na grade (requisito do usuário), e a
  tela sabe disso porque o recorte mora em `customers.profile_image_id`, não em `customer_photos`.
- A ligação perfil → foto de origem é mantida (`profile_photo_id`), para o toque no círculo abrir o
  original inteiro (FR-014).
- Nenhuma detecção de rosto, nem no servidor nem no navegador (FR-012, premissa "sem processamento de
  rosto").

**Rationale**: mover o recorte para o navegador tira o decodificador do servidor sem perder o que a vendedora
pediu — escolher o enquadramento do rosto. O custo é que o enquadramento deixa de ser reprodutível no
servidor: por isso o recorte é gravado como arquivo, e não como coordenada. 512 px cobre o avatar maior
(128 px) em telas 4x.

**Alternatives considered**:
- *Guardar a coordenada do recorte e cortar no servidor*: exigiria o decodificador da R1.
- *Só CSS (`object-position`)*: não permite zoom nem reencuadre fino, e o círculo mostraria sempre o
  centro.
- *ChatGPT recortar*: o plugin recebe `download_url`, não tem canvas. Por isso o perfil vindo da conversa
  fica sem recorte dedicado.

## R5 — Onde guardar a foto de perfil

**Decision**: duas colunas nulas em `customers`:

| Coluna | Para quê |
|---|---|
| `profile_photo_id` | FK → `customer_photos`: a foto do álbum de onde o perfil saiu (FR-014, e a regra de "excluir a foto tira o perfil") |
| `profile_image_id` | FK → `stored_images`: o recorte quadrado feito no navegador; **nulo** quando o perfil veio de um canal sem navegador e usa a própria foto |

**Invariante**: o perfil existe quando `profile_photo_id` está preenchido. `profile_image_id` é
consequência dele, nunca independente — garantido pelo serviço, e não por check constraint (a 004 não usa
constraints desse tipo em colunas dependentes; o custo de manter a constraint sincronizada com a regra do
serviço é maior que o ganho).

**Rationale**: a relação é 0..1 por cliente (FR-011) e a lista de clientes precisa do avatar em toda linha;
colunas em `customers` evitam JOIN na listagem e tornam "uma foto de perfil por cliente" estrutural.

**Alternatives considered**: *flag `is_profile` em `customer_photos`* — não tem onde pendurar o recorte
feito no navegador, e listar clientes passaria a depender de um JOIN filtrado.

## R6 — Deduplicação e exclusão com objetos compartilhados

**Decision**:
- `stored_images` segue único por `(user_id, sha256)` (004). Uma foto repetida **na mesma cliente** bate em
  `UNIQUE (customer_id, image_id)` em `customer_photos`, e a inclusão devolve a foto existente com
  `created = false` (FR-006, cenário US2-5).
- A mesma imagem em **outra** cliente cria outra `customer_photo` apontando para o mesmo `stored_image`
  (edge case "mesma foto em duas clientes"). Na conversa, o resultado da tool avisa isso ao modelo.
- Ao excluir uma foto, ou trocar/remover o perfil, o serviço apaga a linha de metadado primeiro e depois o
  objeto do storage **somente se ficou órfão**: sem referência em `customer_photos.image_id`,
  `customers.profile_image_id` nem `order_items.image_id`.
- O recorte antigo do perfil é sempre candidato a órfão ao trocar o perfil — ele não está na lista de
  fotos e não é usado por pedido.
- Escrita em duas tabelas (`customer_photos` + `customers`) na mesma transação (padrão existente:
  `ImportRunRepository` é o único precedente de transação na Infra).

**Rationale**: cumpre FR-009 e o edge case de fotos compartilhadas sem cascata (Princípio V) — a exclusão é
feita pelo serviço, e as FKs são `ClientSetNull`.

**Efeito colateral a registrar**: `IImageService.DeleteAsync` (004) apaga objeto e metadado **sem** checar
referência. Esta feature não mexe nele: as fotos usam um caminho próprio (`DeleteIfOrphanAsync` no serviço
de fotos) porque precisa da checagem. Deixar `DeleteAsync` como está evita alterar comportamento de
imagem de produto.

## R7 — Como a tela envia a foto

**Decision**: dois passos, ambos com rota que já existe ou é deste feature:

1. `POST /images` (multipart, `[RequestSizeLimit]`, já publicado pela 004) → `imageId`.
2. `POST /customers/{customerId}/photos` com `{ imageId, description?, source? }` → foto no álbum.

Para várias fotos arrastadas, a tela faz um par de requisições por arquivo, **3 em paralelo**, cada uma
com seu indicador de estado.

**Rationale**: o multipart da 004 já faz tamanho, tipo declarado, hash, dedup e isolamento por usuário —
repetir isso numa rota de fotos seria duplicar a superfície de upload. Separar bytes de metadado também é
o que permite o recorte do perfil usar exatamente o mesmo caminho de envio (R4), sem rota nova.

**Alternatives considered**:
- *Fluxo pré-assinado (`POST /images/uploads` → `PUT` no storage → confirmar)*: não passa o arquivo pelo
  servidor e é o caminho "oficial" da 004 para o navegador, mas exige `crypto.subtle` para o SHA-256 no
  cliente e três idas por foto. Como já há `POST /images` publicada e testada, a rota curta ganhou.
- *Uma rota `POST /customers/{id}/photos` multipart própria*: faria a tela economizar uma chamada, mas
  duplicaria em código novo toda a validação de upload da 004.

## R8 — Paginação da grade

**Decision**: paginação por **cursor** (keyset) ordenada por `created_at DESC, customer_photo_id DESC`.
`GET /customers/{id}/photos?cursor={photoId}&pageSize=24&favorites=true`. A resposta traz `items` e
`nextCursor` (nulo no fim).

**Rationale**: inalterado pela revisão. A grade tem rolagem infinita (cenário US1-7) e fotos entram no topo
enquanto a vendedora rola (arraste, conversa). Com `page/pageSize`, cada foto nova empurra um item para a
página seguinte e duplica fotos na grade. O cursor não sofre isso. O resto da API usa `page`, mas nenhuma
outra listagem tem inserção no topo durante a navegação.

## R9 — Frontend: abas, visualizador, recorte e arraste

**Decision**:
- **Abas**: componente `Tabs` do shadcn/ui sobre `@radix-ui/react-tabs` — dependência que já é a base de
  `Avatar`, `Select` e `Separator` daqui. A aba ativa vai para a URL (`?aba=pedidos`; padrão = Fotos).
- **Visualizador**: o `Modal` existente em tela cheia, `object-contain`, setas ← → no teclado e gesto de
  deslizar por pointer events. Sem biblioteca nova.
- **Recorte**: **`react-easy-crop` 5.x** (MIT, ~9 KB gzip, sem dependências), `aspect={1}` +
  `cropShape="round"`, zoom por pinça ou roda. `onCropComplete` dá `croppedAreaPixels` no espaço da imagem
  exibida (já orientada, R2); um `<canvas>` de 512 aplica o recorte e `toBlob('image/webp', 0.9)` produz o
  arquivo que sobe.
- **Arraste**: eventos nativos `dragenter/dragover/drop` na aba e `<input type="file" multiple
  accept="image/jpeg,image/png,image/webp">` escondido, aberto pelo toque (celular). Sem biblioteca.
- **Envio**: Fetch API (Princípio II). `apiRequest` recebe uma opção `formData` (não serializa JSON nem
  define `Content-Type`, deixando o boundary ao navegador); o progresso por foto é por **estado**
  (enviando, concluída, erro com motivo), porque a Fetch API não expõe progresso de upload.
- **Rolagem infinita**: `IntersectionObserver` numa sentinela no fim da grade, com `loading="lazy"`.
- **Favorita**: atualização otimista do coração, desfeita com aviso (sonner) se a API recusar (SC-008).

**Alternatives considered**: recorte próprio com pointer events (mais caro que a dependência, sobretudo
pinça e acessibilidade); `react-dropzone` (só encapsula os eventos nativos já usados); biblioteca de
lightbox (traz o que a spec não pede e duplicaria o `Modal`).

## R10 — URLs temporárias na tela

**Decision**: a listagem traz, por foto, `url` (o original) e `urlExpiresAt`. A renovação de uma foto
isolada usa **`GET /images/{imageId}`, que já existe** (004); nenhuma rota nova de renovação é criada. Na
tela:
- O contexto guarda `urlExpiresAt` por página. Ao abrir uma foto ou rolar até onde os links venceram (ou
  faltam menos de 30 s), recarrega aquela página pelo mesmo cursor.
- `onError` numa `<img>` chama `imageService.getReadUrl(imageId)` — que já cacheia por `imageId` até perto
  de `urlExpiresAt` e compartilha prometidas em voo.

**Rationale**: reaproveitar o mecanismo de cache da tela de pedido (`useStoredImageUrl`) em vez de
reinventar renovação por foto. Bônus: mantém o `ParityTests` sem exceção nova, porque não há rota de foto
avulsa para ficar sem ferramenta.

## R11 — MCP: arquivos da conversa e confirmação

**Decision**:
- `anexar_fotos_cliente` declara `_meta["openai/fileParams"] = ["fotos"]` com o atributo dedicado
  `AmbrosioFileParamsAttribute`, publicado pelo `ToolDescriptorFilter` (o mesmo caminho de
  `_meta["ambrosio/endpoint"]`).
  **Dois motivos, um por vez, ambos medidos em código:** (1) a 007 **nunca declarou** `fileParams` — não
  havia precedente a reutilizar, ao contrário do que esta página dizia; (2) o `[McpMeta]` do SDK, que a
  007 usa em `ProfileTools.cs:51` para `openai/profile`, **não serve** aqui: a assinatura é
  `(string, string?)` e o contrato da OpenAI pede uma lista. Compilar
  `[McpMeta("openai/fileParams", new[] { "fotos" })]` devolve
  `error CS1503: Argumento 2: não é possível converter de "string[]" para "string?"`.
  `fotos` é um array de `FileArgument` (**até 10 por chamada**), baixado pelo `SafeFileDownloader`
  existente e gravado por `IImageService.StoreFromServerAsync(..., ImageSource.ChatGpt)`.
- Resultado por foto: `guardada`, `ja_existia` (mesma cliente), `guardada_tambem_em_outra_cliente` (aviso)
  ou `recusada` com o motivo da 004. A chamada falha inteira só se a cliente não existir (FR-028).
- Confirmação (`confirmar`, padrão `WriteToolHelper`): **obrigatória** em `anexar_fotos_cliente` (a prévia
  mostra cliente, quantidade, tipos e tamanhos) e em `excluir_foto_cliente` (FR-027). As reversíveis
  (`favoritar_foto_cliente`, `desfavoritar_foto_cliente`, `descrever_foto_cliente`, `definir_foto_perfil`,
  `remover_foto_perfil`) executam direto.
- Escopos: leitura com `customers:read`; escrita com `images:write` — **escopo que já existe**
  (`ScopeSeeder.ScopeNames` da 004/007). Nenhum escopo novo é criado.
- Uma tool por operação REST (`[AmbrosioEndpoint]`), sem exceção de paridade (R10 não criou rota avulsa).
- `obter_cliente` e `listar_clientes` passam a informar `temFotoPerfil` e, no detalhe, `totalFotos`, sem
  URLs — o modelo não precisa delas.

**Alternatives considered**: uma tool `atualizar_foto_cliente` com descrição e favorita opcionais — mais
compacta, mas mapearia para duas rotas e quebraria a paridade 1:1.

## R12 — Origem da foto

**Decision**: `customer_photos.source` (`CustomerPhotoSource`): `Web = 1` (tela), `Api = 2` (integração),
`ChatGpt = 3` (conversa). No corpo de `POST /customers/{id}/photos`, `source` aceita só `web` ou `api`
(padrão `api`); o frontend envia `web`. A conversa grava `ChatGpt` sem passar pelo campo.

**Rationale**: FR-002 pede a origem com os três canais. É campo informativo, sem efeito em permissão.
`ChatGpt` não é aceito na rota para que uma integração não se passe pela conversa.

## R13 — Exclusão da cliente e da conta

**Decision**: não há exclusão de cliente hoje (não existe `DELETE /customers/{id}`) e esta feature **não**
cria uma. O edge case "exclusão da cliente" fica registrado como regra para quando ela existir: o serviço
de clientes chama `DeleteAllForCustomerAsync` (criado aqui, usado também pela exclusão de conta). A
exclusão de conta (`IImageService.DeleteAllForUserAsync`, existente) passa a zerar as colunas de perfil e
apagar `customer_photos` antes de remover as `stored_images`, porque as FKs novas não têm cascata.

## R14 — Testes

**Decision**:
- **API** (`Ambrosio.ApiTests`, skill `dotnet-test-api`), `CustomerPhotosControllerTests`, contra a
  homologação. **Sem gerar imagem em tempo de teste**: usa os fixtures já versionados
  (`Fixtures/Images/small.jpg`, `small.png`, `small.webp`, `other.jpg`) com a técnica de suíte existente de
  acrescentar um sufixo GUID (`UniqueBytes`) para o hash não colidir com outra execução.
  - Envio, dedup na mesma cliente (`created=false`) e compartilhamento entre duas clientes.
  - Tipo recusado pelo mime declarado (`image/gif`) com a mensagem da 004 — **não** há teste de conteúdo
    disfarçado, porque a validação por conteúdo não existe (R2).
  - Acima de `MaxUploadBytes` (413/400), corpo vazio, campo ausente.
  - Descrição: até 500, acima (400), vazio → `null`, apagar.
  - Favorita/desfavorita idempotentes, filtro `favorites=true`, cursor e `nextCursor` nulo no fim.
  - Perfil: com e sem `imageId`; tocar o perfil abre o original; excluir a foto de origem limpa o perfil;
    trocar o perfil apaga o recorte órfão (`HEAD` 404) e preserva o compartilhado.
  - Isolamento A/B (404 indistinguível) e 401 sem token.
  - `profilePhoto` em `GET /customers` e `GET /customers/{id}`, e em `OrderDetailInfo.customer`.
- **MCP**: `CustomerPhotoToolsTests` (prévia sem gravar, escopo, `_meta["openai/fileParams"]` no
  `tools/list`, resultado por foto) e `ParityTests` com as 8 ferramentas novas + contagem atualizada.
- **Frontend** (Vitest + Testing Library): service (montagem de rota, `formData` sem `Content-Type`),
  contexto (cursor, otimista com rollback), grade (ordem, coração, vazia, lazy), área de arraste (drop,
  `input` no toque, recusa por tipo), visualizador (setas, teclado, descrição com limite), conversão do
  recorte em arquivo (canvas → blob → `POST /images`) e avatar com e sem foto.
- Nada de Playwright ou E2E (Princípio VIII).
