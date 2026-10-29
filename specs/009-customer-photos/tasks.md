---

description: "Tarefas da feature 009: fotos de clientes no estilo rede social (revisão 2026-10-29, sem processamento de imagem no servidor)"
---

# Tasks: Fotos de clientes no estilo rede social

**Input**: documentos de `specs/009-customer-photos/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

> **Regenerado em 2026-10-29** pela revisão de escopo do usuário: nenhum processamento de imagem no
> servidor. As tarefas que existiam para isso (pacote ImageSharp, `IImageProcessingAppService`, normalizar /
> miniatura / recorte no servidor, motivo `Dimensions`, limite de 50 MP, fábrica de imagens de teste, rota
> multipart de fotos e a rota avulsa de renovação por foto) **saíram da lista**. Nada delas fica
> "pendente": deixaram de ser requisito. Ver o bloco de revisão em [research.md](research.md).

**Tests**: obrigatórios pela constituição. O Princípio VII exige testes de API para toda rota nova ou
alterada, e o Princípio VIII exige testes unitários no frontend. O escopo de cada teste está no research R14.
Nenhum teste de unidade no backend e nenhum E2E/Playwright.

**Organization**: tarefas agrupadas por história de usuário da spec (US1 a US6).

**Skills obrigatórias** (Princípio I):
- `dotnet-architecture` (entidade, repositório, serviço, DTO, migração, DI);
- `dotnet-fluent-validation` (validadores);
- `dotnet-test-api` (testes de API e do MCP);
- `react-architecture` (types, service, context, hook, provider);
- `react-modal` e `react-alert` (confirmações e avisos);
- `wsl-docker` (homologação).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história da spec (US1 a US6)
- Caminhos relativos à raiz do repositório (`C:\repos\ambrosio`)

## Premissa estrutural que atravessa todas as tarefas

A feature **não** cria caminho de upload. Os bytes entram pela rota `POST /images` da feature 004 (tela) ou
por `IImageService.StoreFromServerAsync` (conversa). As rotas e tools desta feature só manipulam `imageId` e
as linhas de `customer_photos`. Consequência: `[RequestSizeLimit]`, `ImageRejectionReason` novo, decodificação,
remoção de EXIF e geração de miniatura não aparecem em tarefa alguma.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: dependências do frontend e o componente de abas. **Nenhum pacote novo no backend.**

- [X] T001 Instalar `react-easy-crop@^5` e `@radix-ui/react-tabs@^1` em `frontend/package.json` (`npm install`, com `package-lock.json` atualizado)
- [X] T002 Criar o componente shadcn/ui `Tabs` (`Tabs`, `TabsList`, `TabsTrigger`, `TabsContent`) sobre `@radix-ui/react-tabs` em `frontend/src/components/ui/tabs.tsx`, no estilo dos outros componentes de `components/ui` (Tailwind, `cn`). A aba ativa usa a borda inferior `border-brand`, igual ao cabeçalho "Pedidos" atual de `frontend/src/pages/CustomerProfilePage.tsx` (research R9)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: modelo, migração, repositório, serviço e **inclusão/listagem de foto pela API**. Toda história
precisa de fotos gravadas para ser testada.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Modelo e contratos

- [X] T003 [P] Criar o enum `CustomerPhotoSource` (`Web = 1`, `Api = 2`, `ChatGpt = 3`) com XML doc em pt-BR em `backend/Ambrosio.DTO/Enums/CustomerPhotoSource.cs` (data-model §4)
- [X] T004 [P] Criar os DTOs em `backend/Ambrosio.DTO/CustomerPhotos/`, um por arquivo, todos com `[JsonPropertyName]` camelCase e **sem** `userId` (data-model §6):
  - `CustomerPhotoInfo.cs` (`photoId`, `customerId`, `imageId`, `description?`, `isFavorite`, `isProfile`, `source`, `createdAt`, `updatedAt`, `url`, `urlExpiresAt`);
  - `CustomerPhotoPage.cs` (`items`, `nextCursor?`);
  - `CustomerPhotoListQuery.cs` (`cursor?`, `pageSize` com padrão 24, `favorites`);
  - `CustomerPhotoInsertInfo.cs` (`imageId`, `description?`, `source?`);
  - `CustomerPhotoInsertResult.cs` (`photo`, `created`, `alsoInOtherCustomers`);
  - `CustomerPhotoDescriptionInfo.cs` (`description?`);
  - `ProfilePhotoUpdateInfo.cs` (`photoId`, `imageId?`);
  - `CustomerProfilePhotoInfo.cs` (`photoId`, `imageId`, `originalImageId`, `url`, `originalUrl?`, `urlExpiresAt`)
- [X] T005 [P] Criar a entidade `CustomerPhoto` em `backend/Ambrosio.Domain/Models/CustomerPhoto.cs` (data-model §1): `Id`, `UserId`, `CustomerId`, `ImageId`, `Description?`, `IsFavorite`, `Source`, `CreatedAt`, `UpdatedAt`, navegações `Customer` e `StoredImage`, `static Create(...)` despidos de setter público de `UserId`, `UpdateDescription(string?, DateTime)` normalizando vazio em `null` e `SetFavorite(bool, DateTime)` idempotente
- [X] T006 [P] Acrescentar a foto de perfil em `backend/Ambrosio.Domain/Models/Customer.cs` (data-model §2): `ProfilePhotoId?`, `ProfileImageId?`, navegações `ProfilePhoto` e `ProfileImage`, `SetProfilePhoto(long photoId, long? profileImageId, DateTime now)` e `ClearProfilePhoto(DateTime now)`
- [X] T007 Mapear no `backend/Ambrosio.Infra/Context/AmbrosioContext.cs` (Princípio V, espelhando `ConfigureStoredImage`): `DbSet<CustomerPhoto> CustomerPhotos`, `ConfigureCustomerPhoto` com tabela `customer_photos`, PK `customer_photo_id` (`UseIdentityAlwaysColumn`), colunas snake_case, `source` como `HasConversion<int>`, timestamps `timestamp without time zone`, FKs `fk_user_customer_photo` / `fk_customer_customer_photo` / `fk_stored_image_customer_photo` com `ClientSetNull`, índices `ix_customer_photos_user`, `ix_customer_photos_customer`, `ix_customer_photos_image`, `ux_customer_photos_customer_image` (único), `ix_customer_photos_feed` e `ix_customer_photos_favorites` (parcial `WHERE is_favorite`); e em `ConfigureCustomer` as colunas `profile_photo_id` / `profile_image_id` com FKs `fk_customer_photo_customer` / `fk_profile_image_customer` (`ClientSetNull`) e os índices `ix_customers_profile_photo` / `ix_customers_profile_image`. **Sem check constraint** (data-model §2: a dependência é regra de serviço)
- [X] T008 Gerar a migração: `cd backend && dotnet ef migrations add AddCustomerPhotos -p Ambrosio.Infra -s Ambrosio.API`, conferir no arquivo gerado que tudo é snake_case e que nenhuma FK sai com `onDelete: Cascade`; conferir `Up`/`Down` simétricos e o `AmbrosioContextModelSnapshot.cs` atualizado. Sem banco no ar, `dotnet ef migrations remove` + refazer se precisar (memória de projeto: remove exige `--force` sem banco)
- [X] T009 [P] Criar `backend/Ambrosio.Infra.Interfaces/Repository/ICustomerPhotoRepository.cs` com `Task<CustomerPhoto?> GetByIdAsync(long userId, long customerId, long photoId)`, `Task<List<CustomerPhoto>> ListAsync(long userId, long customerId, long? cursor, int pageSize, bool favorites)` (fetch de `pageSize + 1` para o cursor), `Task<CustomerPhoto?> FindByImageAsync(long userId, long customerId, long imageId)`, `Task<int> CountOtherCustomersUsingAsync(long userId, long customerId, long imageId)`, `Task<long?> CountForCustomerAsync(long userId, long customerId)`, `Task<CustomerPhoto> InsertAsync(CustomerPhoto)`, `Task<Customer> UpdateCustomerAsync(Customer)`, `Task DeleteAsync(CustomerPhoto)` e `Task DeleteWithProfileClearAsync(CustomerPhoto, Customer)` (os dois últimos dentro de **uma transação**, precedente: `ImportRunRepository`)
- [X] T010 Implementar `backend/Ambrosio.Infra/Repository/CustomerPhotoRepository.cs` sobre o `AmbrosioContext` genérico da casa (`ICustomerPhotoRepository<TModel>` se o padrão dos repositórios exigir), sempre com `user_id` como primeira cláusula de toda leitura, `AsNoTracking` onde não há escrita, cursor por `created_at DESC, customer_photo_id DESC` e mapeamento das navegações necessárias ao `CustomerPhotoInfo`
- [X] T011 [P] Acrescentar `Task<bool> IsImageReferencedAsync(long imageId)` em `backend/Ambrosio.Infra.Interfaces/Repository/IStoredImageRepository.cs` e implementar em `backend/Ambrosio.Infra/Repository/StoredImageRepository.cs`: `Any` em `customer_photos.image_id`, `customers.profile_image_id` e `order_items.image_id` (research R6)
- [X] T012 [P] Criar os validadores FluentValidation em `backend/Ambrosio.Domain/Validators/` (skill `dotnet-fluent-validation`, mensagens em pt-BR, sem null-propagation em acesso encadeado): `CustomerPhotoListQueryValidator.cs` (`pageSize` 1–60, `cursor > 0` quando vindo), `CustomerPhotoInsertInfoValidator.cs` (`imageId > 0`, `description ≤ 500`, `source ∈ {web, api}`), `CustomerPhotoDescriptionInfoValidator.cs` (`description ≤ 500`) e `ProfilePhotoUpdateInfoValidator.cs` (`photoId > 0`, `imageId > 0` quando vindo)

### Serviço e primeira rota

- [X] T013 Criar `backend/Ambrosio.Domain/Interfaces/ICustomerPhotoService.cs` com as assinaturas da feature inteira (listar, anexar, descrever, favoritar, excluir, definir e remover perfil, `DeleteAllForCustomerAsync`), XML doc em pt-BR dizendo que `userId` vem do claim e que recurso alheio é 404
- [X] T014 Implementar em `backend/Ambrosio.Domain/Services/CustomerPhotoService.cs` o núcleo compartilhado — validação, cliente dono (`ICustomerRepository`), situação `available` da imagem (`IStoredImageRepository`), dedup por `(customer_id, image_id)`, mapeamento para `CustomerPhotoInfo` com URL assinada via `IImageService.GetByIdAsync`/`IObjectStorageAppService` e os motivos de erro da tabela §10 do contrato REST. Construtor só com abstrações (Domain não vê EF nem AWS SDK)
- [X] T015 Registrar em `backend/Ambrosio.Application/Startup.cs`: `ICustomerPhotoRepository<>`→`CustomerPhotoRepository<>` e `ICustomerPhotoService`→`CustomerPhotoService` como scoped, e os validadores de `CustomerPhoto*` (nada de AppService novo)
- [X] T016 Criar `backend/Ambrosio.API/Controllers/CustomerPhotosController.cs` com `[Authorize]`, rota base `customers/{customerId:long}`, `GetPhotos` (`GET photos`) e `Attach` (`POST photos`, 201 com `Location` ou 200 quando já existia), usuário só de `this.GetUserId()` e o mapeamento de exceções do contrato REST §10 — o mesmo padrão de verbos/código de `ImagesController` (nenhum `IFormFile` aqui)
- [X] T017 `cd backend && dotnet build Ambrosio.sln` — o grafo inteiro tem de compor (API, MCP, Infra, DTO) antes de qualquer teste

### Testes de API da fundação

- [X] T018 [P] Acrescentar a `backend/Ambrosio.ApiTests/Helpers/TestDataHelper.cs` os auxílios de foto: `UploadImageAsync(fixture, token, fileName)` (usa `PostFileAsync` em `POST /images` com os fixtures já versionados em `backend/Ambrosio.ApiTests/Fixtures/Images/` + sufixo GUID nos bytes para o hash não colidir com outra execução, devolvendo `imageId`) e `AttachPhotoAsync(fixture, token, customerId, imageId, description?)`. **Nenhuma imagem é gerada em tempo de teste** — sem ImageSharp no projeto de testes (research R14)
- [X] T019 [P] Criar `backend/Ambrosio.ApiTests/Controllers/CustomerPhotosControllerTests.cs` com os casos de anexar e listar (contracts/api.md §1–2, §5–6): 201 novo, 200 com `created=false` na repetição, `alsoInOtherCustomers` quando a mesma imagem vai para outra cliente, ordem mais recente primeiro, `nextCursor` nulo no fim, `pageSize` fora de 1–60 → 400, `imageId` inexistente → 404, `imageId` de outra vendedora → 404, `imageId` `pending` → 400, `source=chatGpt` na rota REST → 400, isolamento A/B (404 indistinguível) e 401 sem token. **Sem asserção de EXIF, dimensão ou miniatura** (fora de escopo)
- [X] T020 Rodar a suíte desta fase: `cd backend && ApiBaseUrl=http://localhost:8080/api dotnet test Ambrosio.ApiTests --filter "FullyQualifiedName~CustomerPhotos"`. **Pré-condição de ambiente**: a stack homolog no ar pelo `wsl-docker`; se o banco homolog não estiver acessível, registrar a tarefa como bloqueada com o motivo (memória de projeto: `AMBROSIO_CONNECTION_STRING` do `.env` tinha porta inválida) em vez de marcar como passada

**Checkpoint**: fotos entram e são listadas pela API com teste verde (ou bloqueio registrado).

---

## Phase 3: User Story 1 + 2 — grade e arraste (P1)

### Backend (o que falta para a história ser testável)

- [X] T021 [P] [US1] Completar em `backend/Ambrosio.Domain/Services/CustomerPhotoService.cs` a listagem com cursor e filtro `favorites`, incluindo `isProfile` (comparando com `customers.profile_photo_id`) e `url`/`urlExpiresAt` de cada original (contracts/api.md §1)
- [X] T022 [P] [US2] Completar em `backend/Ambrosio.Domain/Services/CustomerPhotoService.cs` a deduplicação: corrida de `ux_customer_photos_customer_image` resolvida por `DuplicateException` → relê e devolve a existente com `created=false` (research R6, FR-006)
- [X] T023 [US1][US2] Testes de API em `backend/Ambrosio.ApiTests/Controllers/CustomerPhotosControllerTests.cs`: `thumbnailUrl` **não existe** (asserir a ausência é o teste de que a grade usa o original), `url` abre o mesmo hash do arquivo enviado, `favorites=true` corta, cursor paginado com 25 fotos, e a página de `GET /customers/{id}/photos` não faz nenhuma chamada ao storage além da assinatura local

### Frontend

- [X] T024 [P] [US2] Acrescentar `formData?: FormData` a `RequestOptions` em `frontend/src/Services/apiClient.ts`: quando presente, o corpo é o `FormData` (sem `JSON.stringify`) e **nenhum `Content-Type` é enviado** — o boundary fica com o navegador. Conferir `getHeaders` em `frontend/src/Services/apiHelpers.ts` (a flag de corpo precisa continuar false para `FormData`) e manter o tratamento de 401/404/400 idêntico
- [X] T025 [P] [US1] Criar `frontend/src/types/customerPhoto.ts` (`interface`, camelCase): `CustomerPhotoInfo`, `CustomerPhotoPage`, `CustomerPhotoListParams`, `CustomerPhotoInsertResult`, `CustomerProfilePhotoInfo` e `CustomerPhotoSource` (`'web' | 'api' | 'chatGpt'`)
- [X] T026 [US1] [P] Substituir `photoUrl?: string | null` por `profilePhoto?: CustomerProfilePhotoInfo | null` em `frontend/src/types/customer.ts` e atualizar os consumidores (`CustomerCard.tsx`, `CustomerProfilePage.tsx`, `OrderDetailPage.tsx`) para `customer.profilePhoto?.url`
- [X] T027 [P] [US2] Acrescentar `upload(file: File)` a `frontend/src/Services/imageService.ts` — `POST /images` com `formData` (T024), devolvendo `ImageInfo` — e manter o cache de `getReadUrl` como está (é o que a renovação das fotos usa, R10)
- [X] T028 [US1][US2] Criar `frontend/src/Services/customerPhotoService.ts` (classe singleton sobre `apiRequest`, skill `react-architecture`): `list(params)`, `attach(customerId, imageId, description?)`, `setDescription`, `favorite`/`unfavorite`, `remove`, `setProfile(customerId, photoId, imageId?)`, `removeProfile`
- [X] T029 [US1][US2] Criar `frontend/src/Services/customerPhotoService.test.ts` cobrindo montagem de rota/query (cursor, `favorites`, `pageSize=24`), corpo JSON de `attach` e a propagação das classes de erro do transporte — espelhando `frontend/src/Services/customerService.test.ts` (mock só em `fetch`)
- [X] T030 [US1][US2] Criar `frontend/src/Contexts/CustomerPhotoContext.tsx` e `frontend/src/hooks/useCustomerPhotos.ts`: páginas acumuladas por cursor, `loadingMore`, anexação com fila de até 3 em paralelo e estado por item (`enviando` → `registrando` → `concluída`/`erro com motivo`), `created=false` descartando a célula com aviso, favorite otimista com rollback, exclusão, e `UnauthorizedError` ignorado (é o `AuthContext` quem desloga). Registro do provider no array de `frontend/src/main.tsx`, dentro de `CustomerProvider`
- [X] T031 [US1][US2] Criar `frontend/src/Contexts/CustomerPhotoContext.test.tsx` cobrindo cursor, inserção no topo, dedup, rollback da favorita e exclusão
- [X] T032 [P] [US1] Criar `frontend/src/components/customerPhotos/CustomerPhotoGrid.tsx` (contracts/ui.md §2): 3 colunas `gap-0.5`, célula `aspect-square` com `<img src={photo.url}>`, `object-cover`, `loading="lazy"`, `decoding="async"`, `alt` com a descrição ou "Foto de \<nome\> de \<data\>", coração nas favoritas, sentinela de `IntersectionObserver`, `Skeleton` de 9 células no primeiro carregamento, e estado vazio com a área de inclusão
- [X] T033 [P] [US2] Criar `frontend/src/components/customerPhotos/CustomerPhotoDropzone.tsx` (contracts/ui.md §3): texto **"Arraste para incluir uma foto"** (FR-020) / "Toque para incluir uma foto" em `pointer: coarse`, `dragenter/dragover/drop` nativos com destaque, `<input type="file" multiple accept="image/jpeg,image/png,image/webp" hidden>` no clique, recusa local de tipo fora da lista e de tamanho acima de `MaxUploadBytes` com o nome do arquivo no aviso, e miniatura local por `URL.createObjectURL` com `URL.revokeObjectURL` na limpeza
- [X] T034 [P] [US1] Criar `frontend/src/components/customerPhotos/CustomerPhotoViewer.tsx` (contracts/ui.md §4) sobre o `Modal` existente: **original** em `object-contain`, setas ‹ ›, teclas ← →, `Esc`, deslizar por pointer events, `onError` na `<img>` chamando `imageService.getReadUrl(imageId)` uma vez (R10). Nesta fase sem edição de descrição nem perfil (US3/US4 ligam isso)
- [X] T035 [US1][US2] Reestruturar `frontend/src/pages/CustomerProfilePage.tsx`: `Tabs`/`TabsList`/`TabsTrigger`/`TabsContent` com **Fotos** (padrão) e **Pedidos**; aba na URL (`?aba=pedidos`, `useSearchParams`, padrão sem parâmetro); o bloco de pedidos atual vai inteiro para dentro da segunda aba sem mudar nada; área de inclusão e grade na primeira. Teste em `frontend/src/pages/CustomerProfilePage.test.tsx`
- [X] T036 [P] [US1][US2] Adicionar o namespace `customerPhotos` a `frontend/src/i18n/locales/pt-BR.ts` com todas as strings visíveis da feature (contracts/ui.md, FR-024 da constituição §IV): rótulos de aba, arraste, vazia, favoritas/todas, descrição, perfil, exclusão, e os motivos de erro mostrados à vendedora
- [X] T037 [US1][US2] Testes de componente: `CustomerPhotoGrid.test.tsx`, `CustomerPhotoDropzone.test.tsx` e `CustomerPhotoViewer.test.tsx` em `frontend/src/components/customerPhotos/`
- [X] T038 Rodar o gate do frontend desta fase: `cd frontend && npm run lint && npm run type-check && npm test`

**Checkpoint (MVP)**: a vendedora arrasta fotos e as vê na grade da aba Fotos, com URL renovando sozinha.

---

## Phase 4: User Story 3 — foto de perfil (P1)

### Backend

- [X] T039 [US3] Implementar `SetProfileAsync` em `backend/Ambrosio.Domain/Services/CustomerPhotoService.cs` (contracts/api.md §6, research R4/R5): valida `photoId` da cliente (senão 404 `Foto não encontrada.`), aceita `imageId` opcional do recorte feito no navegador (conferindo `available` e dono), grava `profile_photo_id` + `profile_image_id` numa transação via `ICustomerPhotoRepository`, e marca o recorte anterior como candidato a órfão **depois do commit**
- [X] T040 [US3] Implementar `RemoveProfileAsync` e a parte de perfil de `DeleteAsync` (excluir a foto de origem limpa o perfil, FR-015 e edge case), com `DeleteIfOrphan` usando `IsImageReferencedAsync` (T011) e `IImageService.DeleteAsync` só quando ficou órfão
- [X] T041 [US3] Expor `PUT customers/{customerId}/profile-photo` e `DELETE customers/{customerId}/profile-photo` em `backend/Ambrosio.API/Controllers/CustomerPhotosController.cs`
- [X] T042 [US3] Preencher `profilePhoto` em `backend/Ambrosio.DTO/Customers/CustomerInfo.cs` (+ `photoCount` em `CustomerDetailInfo.cs`) e o mapeamento em `backend/Ambrosio.Domain/Services/CustomerService.cs` — na listagem sem `originalUrl`, uma URL assinada por linha (data-model §6). Testar que a listagem não explode o custo: `photoCount` por `COUNT` agrupado, não por consulta por cliente

### Frontend

- [X] T043 [P] [US3] Criar `frontend/src/lib/cropToFile.ts` (`cropToFile(img, croppedAreaPixels, size, mimeType)` → `File`) desenhando o recorte num `<canvas>` de 512 × 512 e `toBlob`; com `frontend/src/lib/cropToFile.test.ts` cobrindo lado, formato pedido e a recusa quando `HTMLCanvasElement.prototype.toBlob` não existe (jsdom não implementa — stub explícito no teste)
- [X] T044 [US3] Criar `frontend/src/components/customerPhotos/ProfilePhotoCropper.tsx` (contracts/ui.md §5): `react-easy-crop` com `aspect={1}`, `cropShape="round"`, `showGrid={false}`, `objectFit="contain"`, zoom 1×–4× por pinça/roda/slider, quadrado inicial centrado (nenhum enquadramento é relido do servidor, R4), e salvar em dois passos — `imageService.upload(blob)` → `customerPhotoService.setProfile(customerId, photoId, imageId)`. Botão "Remover foto de perfil" quando a foto é o perfil atual
- [X] T045 [US3] Ligar o visualizador e o cabeçalho: "Usar como foto de perfil" no `CustomerPhotoViewer.tsx` abre T044 (rótulo "Ajustar enquadramento" quando já é o perfil), e `CustomerAvatar` do cabeçalho vira botão que abre a **original** da foto de origem (FR-014, US3-4) — só clicável com perfil definido (US3-5). Capa superior do avatar continua `rounded-full`
- [X] T046 [US3] Testes: `ProfilePhotoCropper.test.tsx` em `frontend/src/components/customerPhotos/` e os casos novos de `frontend/src/pages/CustomerProfilePage.test.tsx` (avatar clicável só com perfil, troca de perfil atualizando cabeçalho e lista)
- [X] T047 [US3] Testes de API em `backend/Ambrosio.ApiTests/Controllers/CustomerPhotosControllerTests.cs`: perfil com e sem `imageId`; `photoId` de outra cliente → 404; excluir a foto de origem limpa o perfil e o recorte sai do storage (`GET /images/{imageId}` → 404); trocar de perfil apaga o recorte velho órfão e **preserva** o compartilhado com item de pedido; `DELETE profile-photo` mantém a foto no álbum; `GET /customers` e `GET /customers/{id}` com `profilePhoto` e `photoCount`; e `OrderDetailInfo.customer.profilePhoto`

**Checkpoint**: avatar redondo com recorte escolhido pela vendedora, sem uma linha de decodificação no servidor.

---

## Phase 5: User Story 4 — descrição, favorita e exclusão (P2)

- [X] T048 [US4] Implementar `UpdateDescriptionAsync`, `SetFavoriteAsync` e `DeleteAsync` em `backend/Ambrosio.Domain/Services/CustomerPhotoService.cs` (FR-007/008/009): `Trim`, vazio → `null`, favorita idempotente sem tocar `updated_at`, exclusão com limpa-perfil e `DeleteIfOrphan`
- [X] T049 [US4] Expor no `backend/Ambrosio.API/Controllers/CustomerPhotosController.cs`: `PUT photos/{photoId}/description`, `PUT photos/{photoId}/favorite`, `DELETE photos/{photoId}/favorite`, `DELETE photos/{photoId}` (204)
- [X] T050 [P] [US4] Criar `frontend/src/components/customerPhotos/CustomerPhotoDescription.tsx`: `textarea` com contador `n/500`, Salvar/Cancelar, `Ctrl/Cmd+Enter` salva, "Editar"/"Adicionar descrição" e limite travando o botão com aviso (FR-007, US4-5)
- [X] T051 [US4] Ligar no `frontend/src/components/customerPhotos/CustomerPhotoViewer.tsx`: coração `aria-pressed` otimista com rollback e aviso (SC-008), descrição (T050), "Usar como foto de perfil" já ligado na US3, e **Excluir** com o `ConfirmModal` existente — texto extra quando a foto é o perfil ("A Maria vai voltar a aparecer com as iniciais."). Depois de excluir, mostrar a próxima ou fechar (contracts/ui.md §4, FR-019)
- [X] T052 [US4] Testes de API em `backend/Ambrosio.ApiTests/Controllers/CustomerPhotosControllerTests.cs`: descrição de 500 (ok) e 501 (400 com `erros[0].campo == "description"`), apagar descrição → `null`, favoritar/desfavoritar idempotentes, `favorites=true` refletindo, exclusão removendo o objeto do storage quando órfão e preservando quando a mesma imagem está em outra cliente ou em item de pedido, e 404 nos dois recursos alheios
- [X] T053 [US4] `CustomerPhotoDescription.test.tsx` e os casos novos de `CustomerPhotoViewer.test.tsx` (coração otimista com `fetch` falhando dá rollback + toast; excluir pede confirmação; setas continuam andando depois de excluir)
- [X] T054 Rodar o gate: `cd frontend && npm run lint && npm run type-check && npm test`

---

## Phase 6: User Story 5 — conversa no ChatGPT (P2)

- [X] T055 [US5] Criar `backend/Ambrosio.McpServer/Mcp/Tools/CustomerPhotoTools.cs` com as 8 ferramentas de `contracts/mcp-tools.md` sobre o **mesmo** `ICustomerPhotoService` da API e o `WriteToolHelper` (`confirmar`) existente: `listar_fotos_cliente`, `anexar_fotos_cliente`, `descrever_foto_cliente`, `favoritar_foto_cliente`, `desfavoritar_foto_cliente`, `definir_foto_perfil`, `remover_foto_perfil`, `excluir_foto_cliente`. `[AmbrosioScopes]` = `customers:read` na leitura e `images:write` (já existe) nas escritas; `[AmbrosioEndpoint]` nominal igual à rota REST; argumentos e saída em pt-BR com snake_case
- [X] T056 [US5] `anexar_fotos_cliente`: receber `fotos: FileArgument[]` (**até 10**), baixar cada um pelo `SafeFileDownloader` existente e gravar por `IImageService.StoreFromServerAsync(..., ImageSource.ChatGpt)` — o mesmo corpo de `enviar_imagem`; resultado por foto `guardada | ja_existia | guardada_tambem_em_outra_cliente | recusada(motivo da 004)`; um arquivo recusado não interrompe os demais; `cliente_id` inexistente não grava nada (FR-028). A ambiguidade de nome fica com `listar_clientes`/`buscar_cliente`, porque esta ferramenta recebe id
- [X] T057 [US5] Declarar `openai/fileParams` em `anexar_fotos_cliente` com o atributo `AmbrosioFileParamsAttribute` + publicação no `ToolDescriptorFilter`. **Medido:** `[McpMeta]` do SDK existe (a 007 o usa em `ProfileTools.cs:51` para `openai/profile`), mas aceita só `string?` — `new[] { "fotos" }` produz `error CS1503`; o contrato OpenAI exige lista, daí o atributo dedicado. A 007 nunca declarou `fileParams`, então não havia precedente. Conferir que sai no `tools/list`
- [X] T058 [US5] `definir_foto_perfil` na conversa: sem recorte (não há navegador) — ligar `contracts/mcp-tools.md §6`, reaproveitando o recorte existente quando a foto informada é a mesma de que ele saiu, e devolvendo o `aviso` de "ficou sem recorte quadrado"
- [X] T059 [P] [US5] Acrescentar `temFotoPerfil` (e `totalFotos` no detalhe) no mapeador compartilhado `backend/Ambrosio.McpServer/Mcp/Results/ToolMapping.cs`, usado por `obter_cliente`, `listar_clientes` e `buscar_cliente` — sem nenhuma URL assinada na saída. (O texto original apontava `CustomerTools.cs`; os três saem pelo `ToolMapping`.) Consequência verificada: `InsightService` monta `CustomerInfo` por conta própria em `buscar_cliente` por CPF e no bloco `cliente` de `historico_cliente`, e precisa preencher `ProfilePhoto` também
- [X] T060 [US5] Atualizar `backend/Ambrosio.ApiTests/McpServer/ParityTests.cs`: contagem de ferramentas de 29 para **37** e as 8 novas `customer_photo` no mapa de paridade. **Nenhuma exceção nova** em `OperationsWithoutTool`: a rota avulsa de foto não existe, porque a renovação usa `GET /images/{imageId}` (R10)
- [X] T061 [US5] Criar `backend/Ambrosio.ApiTests/McpServer/CustomerPhotoToolsTests.cs` sobre `McpTestClient`: prévia sem gravar, gravar com `confirmar=true`, recusa de tipo com a mensagem **da 004**, falta de escopo `images:write` virando desafio, `_meta.openai/fileParams` no `tools/list`, `excluir_foto_cliente` recusando sem `confirmar`, e o resultado por foto de um lote com um arquivo inválido (research R14)
- [X] T062 [US5] Documentar no `plugin/skills/vendas-ambrosio/SKILL.md` a seção "Fotos das clientes" com a regra de confirmar antes de anexar/excluir e o aviso de que o enquadramento do perfil se faz na tela (caminho real conferido no repositório antes de escrever)

---

## Phase 7: User Story 6 — API para integrações (P2)

- [X] T063 [US6] Completar o que faltou para a superfície REST ficar par com a conversa e conferir o contrato rota a rota contra `contracts/api.md` (`GET photos`, `POST photos`, os dois `PUT` de descrição/favorita, os dois `DELETE` de favorita/foto, `PUT`/`DELETE profile-photo`)
- [X] T064 [P] [US6] Criar a coleção Bruno `bruno/Customers/Fotos/` com as 8 operações de foto (5 caminhos), no formato dos `.bru` existentes e com `{{token}}`/variáveis do ambiente de homolog, seguindo a convenção das features 004/007/008
- [X] T065 [P] [US6] Documentar em `backend/README.md` as rotas de fotos de cliente e a origem dos bytes (`POST /images` da 004), **sem** seção de dependência nova. **Cuidado**: `backend/README.md` tem alterações não commitadas da feature 008 — acrescentar a minha seção sem reescrever as linhas alheias (reread antes de editar)

---

## Phase 8: Polish & Cross-Cutting

- [X] T066 [P] Estender `ImageService.DeleteAllForUserAsync` (ou o ponto de exclusão de conta equivalente) para zerar as colunas de perfil das clientes do usuário e apagar as linhas de `customer_photos` **antes** de remover as `stored_images` — as FKs novas não têm cascata (research R13)
- [X] T067 [P] Expor `DeleteAllForCustomerAsync` no serviço (usada pela exclusão de conta e pela futura `DELETE /customers/{id}`), sem rota nova
- [X] T068 [P] Atualizar `docs/historias/010-banco-imagens-clientes-chatgpt.md`: Status, link para a spec 009 e o que ficou fora (tags; e agora também a remoção de EXIF, que a HU 010 previa)
- [X] T069 Conferir os edge cases de `spec.md` que não têm teste próprio: "duas abas abertas" (a favorita vem da API, sem cache em storage — nada a fazer além de não cachear `isFavorite`), "envio interrompido" (a célula provisória some no `unmount` com `URL.revokeObjectURL`) e "link vencido" com o cache de `imageService`
- [ ] T070 `cd backend && dotnet build Ambrosio.sln && dotnet test Ambrosio.ApiTests` contra a stack homolog reconstruída (`wsl -d Ubuntu -- bash -lc "cd /mnt/c/repos/ambrosio && docker compose up -d --build"`) — **Princípio VII exige a suíte inteira**, não só o filtro. Pré-requisito de ambiente: a conexão do banco homolog no `.env`; se estiver inválida, registrar bloqueado com o motivo e não marcar como passada

  **Execução de 2026-10-29 — interrompida pelo usuário, não passou.** Rodada contra a stack homolog
  (imagens já atualizadas) e encerrada às 16:3x, antes do fim, para o usuário validar em produção. Até ali:
  **40 falhas**, nenhuma em teste da 009 (o filtro `CustomerPhotoTools|ParityTests` passou 20/20 antes).
  Causas vistas: ~32 em importação (`Credenciais cifradas não decifram (chave trocada)` — chave AES da 008 no
  contêiner não abre as credenciais do banco homolog); 403 de CORS no bucket do Spaces; um 500 transitório do
  banco; `GetById_Twice` com URLs iguais no mesmo segundo; e duas de `Cleanup_*` em `ImagesControllerTests`
  ainda sem diagnóstico. Também: `Ambrosio.Tests` não compilava (construtores de `CustomerService`/
  `OrderService` sem `ICustomerPhotoService`) — corrigido, 230/230 verdes.
- [X] T071 `cd frontend && npm run lint && npm run type-check && npm test && npm run build`
- [X] T072 Percorrer o `quickstart.md` desta feature (§3 tela, §4 hash do arquivo, §6 conversa) e registrar o que depende de navegador/ChatGPT como verificação humana do usuário, não como "não verificado"

  **Checklist deixado para verificação humana** — nenhum destes posso atestar eu: exigem navegador real,
  sessão no ChatGPT ou os segredos de storage que só existem no shell de quem lança a API:

  1. **§4, prova de que nada é regravado** — SHA-256 do arquivo enviado igual ao do arquivo baixado pela
     `url` da foto, e `GET /images/{imageId}` devolvendo `width: null, height: null`. É a FR-004/SC-003
     revista sendo cumprida de verdade.
  2. **Grade em dados móveis** — abrir uma cliente com ~20 fotos reais em 4G e dizer se a página de 24
     **originais** é aceitável. Sem miniatura (decisão de 2026-10-29), o SC-002 só responde no uso.
  3. **Recorte** — enquadrar o rosto, salvar, e conferir que o avatar do cabeçalho, do card da lista e do
     pedido mostram exatamente a área escolhida; que a original continua na grade; e que tocar no avatar
     abre a original inteira (FR-014).
  4. **Foto girada pelo celular** — mandar uma foto com EXIF de rotação e ver grade, visualizador e o
     editor em pé. A orientação aqui é do navegador (`image-orientation: from-image`): se alguma tela a
     mostrar deitada, o defeito é de markup/CSS, não de decodificação.
  5. **§6, conversa** — no ChatGPT (modo desenvolvedor): prévia com nome/tipo/tamanho sem gravar; depois
     do sim, as fotos entram com origem `chatGpt`; `definir_foto_perfil` devolve o `aviso` de "sem recorte
     quadrado"; e `tools/list` mostra `_meta["openai/fileParams"] = ["fotos"]`.
  6. **Produção** — depois do deploy, conferir o espaço em disco (o deploy da 008 falhou por disco cheio).
     A 009 não acrescenta dependência à imagem: só o bundle novo do frontend.
- [X] T073 Gate final da constituição: nenhum pacote novo no backend, nenhuma rota multipart nova, `dotnet-architecture` respeitada (Domain sem EF/AWS SDK, service fino, controller sem regra), toda string visível no i18n, toda leitura com `user_id` primeiro, e as FRs revogadas/reescritas (FR-003/004/005/012/013) refletidas em spec, research, plan, data-model, contratos e tasks

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2 → Phase 3 (MVP) → Phase 4 → Phase 5 → Phase 6 → Phase 7 → Phase 8.**
- T017 (build) bloqueia todos os testes de API. T020 precisa da stack homolog no ar.
- US1/US2 (Phase 3) dependem de T001–T022; a grade (T032) precisa do contexto (T030) e dos types (T025).
- US3 (Phase 4) depende do repositório (T009/T010), da checagem de órfão (T011) e do upload do service (T027).
- US4 (Phase 5) depende de US1/US2 (as fotos precisam existir).
- US5 (Phase 6) depende do serviço pronto (Phases 2–5), porque as ferramentas chamam o mesmo serviço.
- US6 (Phase 7) depende de todas as rotas publicadas.
- **Sem dependência nova**: não há tarefa de instalar pacote no backend, de AppService de imagem, nem de
  migration de coluna de enquadramento.

### Guia de paralelismo

- Dentro da Phase 2, T003–T006, T009, T011 e T012 são arquivos separados e podem ir em paralelo. T007 e T008
  são sequenciais (o mapeamento produz o snapshot que a migração usa).
- Na Phase 3, T024–T029 separam-se por arquivo; T030 depois de T028; T032–T034 em paralelo; T035 integra tudo.
- Na Phase 4, T043 é independente de T039–T042 (frontend vs. backend).
- Na Phase 6, T059 é o único arquivo separado do resto das ferramentas.

---

## Implementation Notes for the executor

- **Ler as skills obrigatórias antes de codar** (Princípio I). Elas moram em `.claude/skills/<nome>/SKILL.md`
  e a ferramenta `skill` deste ambiente não as resolve — abrir o arquivo à mão.
- **Rerodar os arquivos de referência antes de imitar o padrão**: `ImagesController`, `ImageService`,
  `StoredImageRepository`, `AmbrosioContext.ConfigureStoredImage`, `Startup.cs`, `ImagesControllerTests`,
  `ImageTools`, `ParityTests`, `apiClient.ts`, `customerService.ts`, `CustomerProfilePage.tsx`, `Modal.tsx`.
- **A 004 não é alterada** por esta feature, exceto o acréscimo de `IsImageReferencedAsync` no repositório de
  imagens (T011), que é aditivo. Qualquer outra mexida em arquivo da 004 precisa ser declarada ao usuário.
- **Agora, o que mudou**: a `frontend/src/Services/apiClient.ts` ganha a opção `formData` (T024). É a única
  mudança em infraestrutura compartilhada do frontend, e o `apiClient.test.ts` existente precisa continuar
  verde.
- `git status` no início da feature mostrava alterações não commitadas da feature 008 em `CLAUDE.md`,
  `backend/README.md`, os `appsettings*.json`, `AuthSettings.cs`, `AuthContext.tsx` e seu teste. Elas **não
  são desta feature**: não stagear, não reverter, não reescrever.
- Marcar `[X]` na conclusão de cada tarefa, no arquivo, ao fim da tarefa — não tudo no fim.
