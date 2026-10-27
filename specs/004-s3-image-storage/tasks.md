---

description: "Lista de tarefas — Armazenamento de imagens em storage compatível com S3"
---

# Tasks: Armazenamento de imagens em storage compatível com S3

**Input**: Documentos de design em `specs/004-s3-image-storage/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/images-api.md, quickstart.md

**Tests**: SIM. A spec exige testes de API (FR-024, constituição §VII), criados com a skill `dotnet-test-api` em `backend/Ambrosio.ApiTests`. **Nenhum teste unitário do backend** é criado, ajustado ou executado (constituição §VIII). O frontend não muda de código, então não há testes do frontend.

**Organization**: tarefas agrupadas por história de usuário (US1–US6 da spec). Os testes de cada história são escritos **antes** da implementação e devem falhar até ela existir. Todos rodam contra a homologação (`ApiBaseUrl=http://localhost:8080/api`), que precisa ser reconstruída (`docker compose up -d --build`, skill `wsl-docker`) antes de cada checkpoint.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história de usuário da tarefa (US1–US6)
- Caminhos relativos à raiz do repositório (`C:\repos\ambrosia`)

## Path Conventions

- Backend: `backend/Ambrosio.<Camada>/...` (Clean Architecture, skill `dotnet-architecture`)
- Testes de API: `backend/Ambrosio.ApiTests/...` (skill `dotnet-test-api`)
- Ambientes: `docker-compose*.yml`, `.env*.example`, `appsettings*.json`, `.github/workflows/deploy-prod.yml` (skill `dotnet-env`; Docker pela skill `wsl-docker`)
- Frontend: só `frontend/nginx/` e `frontend/Dockerfile` (CSP)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: linha de base, pacotes novos e verificações de terceiros apontadas no research.

- [X] T001 Registrar a linha de base: rodar `dotnet build backend/Ambrosio.sln` e, com a homologação no ar, `dotnet test backend/Ambrosio.ApiTests` (`ApiBaseUrl=http://localhost:8080/api`); anotar falhas pré-existentes em `specs/004-s3-image-storage/decisions.md` (seção "tasks · baseline"), criando o arquivo
- [X] T002 [P] Escolher e fixar as tags `RELEASE.*` de `minio/minio` e `minio/mc` (nunca `latest`), confirmando com `wsl -e docker pull` que ambas estão disponíveis e que a versão do servidor respeita `MINIO_API_CORS_ALLOW_ORIGIN`; registrar as tags e o resultado em `specs/004-s3-image-storage/decisions.md` (seção "tasks · imagem do storage"). Se nenhuma tag servir, registrar a troca por SeaweedFS ou Garage (research §13) antes de seguir
- [X] T003 [P] Registrar em `specs/004-s3-image-storage/decisions.md` (seção "tasks · licença ImageSharp") a verificação de elegibilidade da Six Labors Split License para o Ambrosio (research §7) e a versão 3.1.x estável escolhida
- [X] T004 Adicionar `AWSSDK.S3` (4.x estável) e `SixLabors.ImageSharp` (3.1.x da T003) a `backend/Ambrosio.Infra/Ambrosio.Infra.csproj`, e `Microsoft.Extensions.Hosting.Abstractions` 9.0.22 a `backend/Ambrosio.Application/Ambrosio.Application.csproj`; `dotnet restore` e `dotnet build` verdes

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: configuração, storage local, tabela, entidade, AppServices, repositório, serviço e controller vazios, que todas as histórias usam.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase terminar.

### Configuração

- [X] T005 [P] Criar `backend/Ambrosio.DTO/Settings/ObjectStorageSettings.cs` com `ServiceUrl`, `PublicServiceUrl` (opcional), `Region` (padrão `us-east-1`), `Bucket`, `ForcePathStyle`, `AccessKey`, `SecretKey`, `PresignedReadMinutes` (15), `PresignedUploadMinutes` (5), `MaxUploadBytes` (10485760), `AllowedMimeTypes` (`List<string>`, vazia por padrão, preenchida pelo appsettings), `PendingTtlMinutes` (60) e `CleanupIntervalMinutes` (15), além da propriedade calculada `EffectivePublicServiceUrl` (= `PublicServiceUrl` ou `ServiceUrl`). XML docs em português no estilo de `CpfProtectionSettings.cs` (research §12)
- [X] T006 Adicionar `ObjectStorage` (tipo `ObjectStorageSettings`) a `backend/Ambrosio.Application/Settings/AmbrosioSettings.cs`; em `backend/Ambrosio.API/Program.cs`, ler a seção `ObjectStorage` e repassá-la ao `ConfigureServices`; em `backend/Ambrosio.Application/Startup.cs`, registrar `services.Configure<ObjectStorageSettings>(...)` copiando todos os campos (depende de T005). A validação no startup fica na US5 (T051)
- [X] T007 [P] Adicionar a seção `ObjectStorage` aos appsettings: `backend/Ambrosio.API/appsettings.json` (padrões de produção, `AllowedMimeTypes: ["image/jpeg","image/png","image/webp"]`, sem segredos), `appsettings.Development.json` (`ServiceUrl: https://nyc3.digitaloceanspaces.com`, `Region: nyc3`, `Bucket: ambrosio`, `ForcePathStyle: false`), `appsettings.Homolog.json` (idem, credenciais vazias vindas do compose) e `appsettings.Production.json` (`Bucket: ambrosia-prod`, credenciais vazias). **Desvio:** o plano pedia credenciais `minioadmin` no `appsettings.Development.json`; como o storage passou a ser o Spaces real, a credencial de Development é de conta e não pode ser versionada — as duas chaves vêm de `ObjectStorage__AccessKey`/`ObjectStorage__SecretKey` no ambiente em todos os quatro arquivos

### Storage (todos os ambientes: DigitalOcean Spaces)

- [X] T008 **Substituído** (decisions.md, seção "tasks · imagem do storage"): não existe serviço `storage` nem `storage-init` no `docker-compose.yml`. As imagens `minio/minio` e `minio/mc` saíram do Docker Hub (`docker pull` responde "repository does not exist"; quay.io e bitnami também não servem) e a decisão do usuário é usar o **Spaces real** em homolog e Development. O compose só injeta as variáveis `ObjectStorage__*`
- [X] T009 No anchor `x-api-environment` de `docker-compose.yml`: `ObjectStorage__AccessKey: ${OBJECT_STORAGE_ACCESS_KEY}`, `ObjectStorage__SecretKey: ${OBJECT_STORAGE_SECRET_KEY}`, `ObjectStorage__Bucket: ${OBJECT_STORAGE_BUCKET:-ambrosio}` e os prazos curtos de homolog `ObjectStorage__PresignedReadMinutes: ${STORAGE_PRESIGNED_READ_MINUTES:-1}`, `ObjectStorage__PresignedUploadMinutes: ${STORAGE_PRESIGNED_UPLOAD_MINUTES:-1}`, `ObjectStorage__PendingTtlMinutes: ${STORAGE_PENDING_TTL_MINUTES:-2}`, `ObjectStorage__CleanupIntervalMinutes: ${STORAGE_CLEANUP_INTERVAL_MINUTES:-1}`. `ServiceUrl`/`Region`/`ForcePathStyle`, que não são segredo, ficam no `appsettings.Homolog.json` (skill `dotnet-env`), sem tocar em nenhuma outra variável
- [ ] T010 Acrescentar a `.env.example` a seção "Storage de imagens" com `OBJECT_STORAGE_ACCESS_KEY`, `OBJECT_STORAGE_SECRET_KEY`, `OBJECT_STORAGE_BUCKET=ambrosio`, `OBJECT_STORAGE_PUBLIC_ORIGIN=https://ambrosio.nyc3.digitaloceanspaces.com` e os quatro `STORAGE_*_MINUTES` comentados com os padrões curtos de homolog; atualizar o `.env` local com as chaves reais (sem versionar) — **`.env.example` pronto; o `.env` local depende das suas chaves**
- [ ] T011 Provisionar o Spaces: script que, com as credenciais do `.env`, garante o bucket `ambrosio`, aplica `PublicAccessBlock`, grava a regra CORS do bucket (`localhost:8080`/`5173`, `GET, PUT, HEAD`, cabeçalho `Content-Type`) e confere que `GET` anônimo a uma chave responde 403 (depende de T010)

### Domínio e dados

- [X] T012 [P] Criar `backend/Ambrosio.DTO/Enums/ImageStatus.cs` (`Pending = 1`, `Available = 2`, `Failed = 3`) e `backend/Ambrosio.DTO/Enums/ImageSource.cs` (`Upload = 1`, `Server = 2`, `ChatGpt = 3`, `Bot = 4`), com XML docs, no padrão de `AuthProvider.cs` (o conversor camelCase global já serializa como `"pending"`, `"chatGpt"` etc.)
- [X] T013 [P] Criar os DTOs de API em `backend/Ambrosio.DTO/Images/`: `ImageUploadInsertInfo.cs` (`mimeType`, `sizeBytes`, `sha256`), `ImageUploadResult.cs` (`imageId`, `alreadyExists`, `uploadUrl?`, `uploadMethod?`, `contentType?`, `uploadExpiresAt?`, `image?`) e `ImageInfo.cs` (`imageId`, `mimeType`, `sizeBytes`, `sha256`, `width?`, `height?`, `status`, `source`, `url?`, `urlExpiresAt?`, `createdAt`), todas as propriedades com `[JsonPropertyName("camelCase")]` e sem `userId` nem `storageKey` (data-model "DTOs")
- [X] T014 [P] Criar os tipos de transporte em `backend/Ambrosio.DTO/Images/`: `ObjectHead.cs` (`ContentLength`, `ContentType?`), `PresignedUrl.cs` (`Url`, `ExpiresAt`), `ImageInspection.cs` (`MimeType`, `Width?`, `Height?`) e `ProcessedImage.cs` (`Content` `byte[]`, `MimeType`, `Width`, `Height`)
- [X] T015 Criar `backend/Ambrosio.Domain/Models/StoredImage.cs` (depende de T012) com as propriedades do data-model, a navegação `User?`, as fábricas `CreatePending(...)` e `CreateAvailable(...)`, as transições `MarkAvailable(width, height, now)` e `MarkFailed(now)` (lançam `InvalidOperationException` fora de `Pending`), `static BuildStorageKey(userId, sha256, mimeType)` → `ambrosio/users/{userId}/images/{sha256}.{ext}` e o mapa de extensões `jpeg→jpg`, `png→png`, `webp→webp` (constante privada). Adicionar `ICollection<StoredImage> StoredImages` em `backend/Ambrosio.Domain/Models/User.cs`
- [X] T016 Mapear `StoredImage` em `backend/Ambrosio.Infra/Context/AmbrosioContext.cs` (depende de T015), no padrão de `Customer`: tabela `stored_images`, PK `stored_image_id` (`UseIdentityAlwaysColumn`, `stored_images_pkey`), colunas e tamanhos do data-model, `status` com default 1, FK `fk_user_stored_image` `ClientSetNull`, índices únicos **parciais** `ux_stored_images_user_sha256_active` e `ux_stored_images_storage_key_active` (`HasFilter("status IN (1, 2)")`) e o índice `ix_stored_images_status_created_at`; adicionar `DbSet<StoredImage> StoredImages`
- [X] T017 Gerar a migração com `dotnet ef migrations add AddStoredImages --project backend/Ambrosio.Infra --startup-project backend/Ambrosio.API` (depende de T016) e revisar o SQL gerado (snake_case, filtros dos índices parciais, sem cascata) em `backend/Ambrosio.Infra/Migrations/*_AddStoredImages.cs`
- [X] T018 [P] Criar as exceções de domínio em `backend/Ambrosio.Domain/Exceptions/`: `ImageRejectedException.cs` (mensagem pronta do contrato, mais uma propriedade `Reason`: `NotUploaded`, `Size`, `Type`, `Hash`, `AlreadyFailed`) e `StorageUnavailableException.cs` (mensagem fixa `"Armazenamento de imagens indisponível."` como constante `MESSAGE`, original só em `InnerException`), no padrão de `DuplicateException.cs`

### Infra

- [X] T019 [P] Criar `backend/Ambrosio.Infra.Interfaces/AppServices/IObjectStorageAppService.cs` (`PutAsync`, `HeadAsync`, `OpenReadAsync`, `DeleteAsync`, `DeletePrefixAsync`, `GetPresignedReadUrl`, `GetPresignedUploadUrl`), `IImageProcessingAppService.cs` (`InspectAsync`, `SanitizeAsync`) e `backend/Ambrosio.Infra.Interfaces/Repository/IStoredImageRepository.cs` (`GetByIdAsync(imageId, userId)`, `GetActiveByHashAsync(userId, sha256)`, `InsertAsync`, `UpdateAsync`, `DeleteAsync`, `ListStaleAsync(pendingBefore, failedBefore, take)`, `DeleteAllByUserAsync(userId)`), com as assinaturas do data-model e XML docs. Em `ListStaleAsync`, documentar a exceção §VI: só o job de limpeza usa, nunca um endpoint
- [X] T020 Implementar `backend/Ambrosio.Infra/AppServices/ObjectStorageAppService.cs` (depende de T004, T014, T018, T019): dois `AmazonS3Client`, um com `ServiceURL = ServiceUrl` para operações e outro com `EffectivePublicServiceUrl` só para assinar, ambos com `ForcePathStyle`, `AuthenticationRegion = Region`, `RequestChecksumCalculation = WHEN_REQUIRED` e `ResponseChecksumValidation = WHEN_REQUIRED` (research §2, §3); `GetPresignedUploadUrl` com `Verb = PUT` e `ContentType` assinado, `GetPresignedReadUrl` com `Verb = GET`, `Expires` a partir dos minutos configurados e `Protocol` pelo esquema do endereço público (research §4, §5); `HeadAsync` devolve `null` em 404; `DeleteAsync` ignora inexistente; `DeletePrefixAsync` com `ListObjectsV2` paginado + `DeleteObjects` em lotes de 1000 (research §10); nunca grava ACL pública. Toda `AmazonServiceException`/`HttpRequestException`/`TaskCanceledException` de rede vira `StorageUnavailableException` sem endpoint, bucket ou chave no `Message`, e nenhuma URL nem credencial vai para log (FR-021)
- [X] T021 Implementar `backend/Ambrosio.Infra/AppServices/ImageProcessingAppService.cs` (depende de T004, T014, T019): `InspectAsync` com `Image.DetectFormatAsync` + `Image.IdentifyAsync` sobre stream seekable, devolvendo `null` para formato não reconhecido e o mime (`image/jpeg`, `image/png`, `image/webp`) com largura/altura; `SanitizeAsync` decodifica, aplica `AutoOrient()`, zera `ExifProfile`, `IptcProfile` e `XmpProfile` (no frame raiz e nos frames), mantém o ICC e re-codifica no mesmo formato (JPEG qualidade 90, PNG, WebP qualidade 90), devolvendo `ProcessedImage` (research §7)
- [X] T022 Implementar `backend/Ambrosio.Infra/Repository/StoredImageRepository.cs` (depende de T016, T019) no padrão de `CustomerRepository.cs`: toda leitura por usuário com `Where(i => i.UserId == userId)` como **primeira** cláusula; `GetActiveByHashAsync` filtra `Status` `Pending` ou `Available`; `InsertAsync` converte `DbUpdateException` com `PostgresException.SqlState == "23505"` em `DuplicateException`; `ListStaleAsync` devolve pendentes com `CreatedAt < pendingBefore` e falhas com `UpdatedAt < failedBefore`, limitado a `take`; `DeleteAllByUserAsync` remove os registros do usuário
- [X] T023 Registrar em `backend/Ambrosio.Application/Startup.cs` (depende de T020–T022): `IStoredImageRepository<StoredImage>` → `StoredImageRepository` (scoped), `IObjectStorageAppService` → `ObjectStorageAppService` (singleton) e `IImageProcessingAppService` → `ImageProcessingAppService` (singleton), nas seções âncora existentes

### Domínio (serviço) e API (esqueleto)

- [X] T024 Criar `backend/Ambrosio.Domain/Interfaces/IImageService.cs` com as assinaturas do data-model e `backend/Ambrosio.Domain/Services/ImageService.cs` (construtor com repositório, `IObjectStorageAppService`, `IImageProcessingAppService`, `IOptions<ObjectStorageSettings>`, `IValidator<ImageUploadInsertInfo>`, `IClock`, `ILogger<ImageService>`) com o mapeamento privado `StoredImage → ImageInfo`: gera `url`/`urlExpiresAt` só quando `Available` (FR-015), e cada método ainda lança `NotImplementedException` até a sua história; registrar `IImageService` → `ImageService` (scoped) em `backend/Ambrosio.Application/Startup.cs`
- [X] T025 [P] Adicionar a `backend/Ambrosio.API/Controllers/ApiControllerBase.cs` os helpers `ImageRejectedError(ImageRejectedException ex)` → 400 `ErrorResult` e `StorageUnavailableError(StorageUnavailableException ex)` → 503 `ErrorResult` com `Erros = []`, com XML docs citando o contrato
- [X] T026 Criar `backend/Ambrosio.API/Controllers/ImagesController.cs` com `[Route("images")]`, `[Authorize]` e construtor com `IImageService`, ainda sem ações (depende de T024, T025); atualizar a `Description` do Swagger em `backend/Ambrosio.API/Program.cs` para mencionar as rotas de imagem
- [X] T027 [P] No projeto de testes, criar `backend/Ambrosio.ApiTests/Helpers/StorageHttp.cs`: `HttpClient` estático para falar direto com o storage (`PutAsync(url, bytes, contentType)`, `GetAsync(url)`, `StripQuery(url)`) e `Sha256Hex(byte[])`; adicionar a `backend/Ambrosio.ApiTests/appsettings.Test.json` a seção `Storage` (`PresignedReadMinutes: 1`, `PresignedUploadMinutes: 1`, `PendingTtlMinutes: 2`, `CleanupIntervalMinutes: 1`, `MaxUploadBytes: 10485760`, `SlowTestMaxWaitMinutes: 5`) e expor esses valores no `backend/Ambrosio.ApiTests/Fixtures/ApiTestFixture.cs`
- [X] T028 [P] Adicionar arquivos de teste em `backend/Ambrosio.ApiTests/Fixtures/Images/`: `small.jpg` (com EXIF GPS), `small.png`, `small.webp`, `fake.jpg` (PDF mínimo renomeado) e `other.jpg` (JPEG diferente de `small.jpg`), todos com menos de 50 KB, e registrar em `backend/Ambrosio.ApiTests/Ambrosio.ApiTests.csproj` com `CopyToOutputDirectory=PreserveNewest`; documentar a origem dos arquivos num `README.md` na mesma pasta

**Checkpoint**: `dotnet build` verde; homolog reconstruída com storage, buckets e migração aplicada; `/images` existe no Swagger, sem operações.

---

## Phase 3: User Story 1 - Enviar uma imagem diretamente ao storage e confirmar (Priority: P1) 🎯 MVP

**Goal**: declarar envio → `PUT` direto no storage → confirmar, com recusa e remoção do que não confere.

**Independent Test**: com usuário autenticado, enviar e confirmar um JPEG válido e abrir a `url` devolvida; enviar um arquivo adulterado e verificar a recusa e a remoção do objeto.

### Tests for User Story 1 ⚠️

> Escrever primeiro em `backend/Ambrosio.ApiTests/Controllers/ImagesControllerTests.cs` (classe nova na coleção da fixture). Devem falhar antes da implementação.

- [X] T029 [US1] Criar `ImagesControllerTests.cs` com o helper privado `RequestAndPutAsync(token, fileName, mimeType)` (declara, faz o `PUT` com `StorageHttp` e devolve `imageId` e bytes) e os testes: declarar JPEG válido → 201 com `uploadUrl`, `uploadMethod = "PUT"`, `contentType` e `uploadExpiresAt` futuro; `PUT` com `Content-Type` diferente → 403 do storage; envio + confirmação → 200 `status = available`, `width`/`height` preenchidos e `GET url` → 200 com os mesmos bytes; repetir a confirmação → 200 idempotente (US1 cenários 1–3)
- [X] T030 [US1] Em `ImagesControllerTests.cs`: confirmar com `sizeBytes` declarado diferente do enviado → 400 (mensagem de tamanho) e `GET` da imagem → `status = failed`; confirmar com `sha256` declarado de outro arquivo → 400 (mensagem de hash); declarar `image/jpeg` e enviar `fake.jpg` → 400 (mensagem de tipo); em cada caso, declarar de novo o mesmo `sha256`/`mimeType` → 201 com `imageId` **novo** (imagens que falharam não deduplicam) e confirmar esse envio novo **sem** fazer o `PUT` → 400 "ainda não foi enviado", o que prova que o objeto recusado (mesma chave) foi apagado do storage (US1 cenários 4–5, critérios 6–7 da HU)
- [X] T031 [US1] Em `ImagesControllerTests.cs`: declarar com `mimeType` `image/gif` → 400 com `erros[].campo = "mimeType"`; `sizeBytes` 0 e acima de `MaxUploadBytes` → 400 em `sizeBytes`; `sha256` com 63 caracteres ou não hex → 400 em `sha256`; confirmar sem ter enviado o arquivo → 400 "ainda não foi enviado" e a imagem continua `pending`; confirmar `imageId` inexistente → 404; enviar e confirmar o mesmo arquivo e declarar de novo → 200 com `alreadyExists = true`, o mesmo `imageId` e `uploadUrl = null`; declarar de novo um hash ainda pendente → 201 com o mesmo `imageId` e uma `uploadUrl` nova (US1 cenários 6–8, critério 8 da HU, edge cases)

### Implementation for User Story 1

- [X] T032 [P] [US1] Criar `backend/Ambrosio.Domain/Validators/ImageUploadInsertInfoValidator.cs` (skill `dotnet-fluent-validation`) recebendo `IOptions<ObjectStorageSettings>`: `MimeType` obrigatório e contido em `AllowedMimeTypes` (sem diferenciar maiúsculas), `SizeBytes` entre 1 e `MaxUploadBytes`, `Sha256` com regex `^[0-9a-fA-F]{64}$`; mensagens em português citando o campo, como nos validators existentes
- [X] T033 [US1] Implementar `ImageService.RequestUploadAsync` em `backend/Ambrosio.Domain/Services/ImageService.cs` (depende de T032): valida; normaliza `sha256` e `mimeType` para minúsculas; `GetActiveByHashAsync` — se `Available`, devolve `(alreadyExists = true, image)` com `Created = false`; se `Pending`, devolve a mesma imagem com `uploadUrl` nova e `Created = true`; senão `StoredImage.CreatePending(..., ImageSource.Upload, now)` + `InsertAsync`, e em `DuplicateException` relê pelo hash e aplica a mesma regra (corrida, research §8); o `uploadUrl` vem de `GetPresignedUploadUrl(key, mimeType, now)`; log só com `imageId`/`userId`
- [X] T034 [US1] Implementar `ImageService.ConfirmUploadAsync` em `backend/Ambrosio.Domain/Services/ImageService.cs` (research §6): `GetByIdAsync` (`NotFoundException` se nulo); `Available` → devolve `ImageInfo` com URL nova; `Failed` → `ImageRejectedException(AlreadyFailed)`; `HeadAsync` nulo → `ImageRejectedException(NotUploaded)` sem mudar o status; `ContentLength` ≠ declarado ou > `MaxUploadBytes` → recusa por tamanho **antes** de baixar; `OpenReadAsync` copiado para `MemoryStream` limitado a `MaxUploadBytes`, com SHA-256 calculado; `InspectAsync` nulo ou mime ≠ declarado ou fora da lista → recusa por tipo; hash ≠ declarado → recusa por hash. Em toda recusa (exceto `NotUploaded` e `AlreadyFailed`): `DeleteAsync(key)`, `MarkFailed(now)`, `UpdateAsync` e depois lançar a exceção. Sucesso: `MarkAvailable(width, height, now)`, `UpdateAsync`, `ImageInfo` com URL de leitura
- [X] T035 [US1] Adicionar a `backend/Ambrosio.API/Controllers/ImagesController.cs` as ações `POST /images/uploads` (`[FromBody] ImageUploadInsertInfo`; `ModelStateError()`; 201 quando `Created`, senão 200; `ProducesResponseType` 201/200/400/503) e `POST /images/uploads/{imageId:long}/confirm` (200/400/404/503), com `try/catch` na ordem `ValidationException` → `ValidationError`, `ImageRejectedException` → `ImageRejectedError`, `NotFoundException` → `NotFoundError`, `StorageUnavailableException` → `StorageUnavailableError`, `Exception` → 500, e XML docs com `<response>` do contrato
- [ ] T036 [US1] Reconstruir a homologação e rodar `dotnet test backend/Ambrosio.ApiTests --filter FullyQualifiedName~ImagesControllerTests` até T029–T031 passarem

**Checkpoint**: envio pelo navegador funcional e protegido (MVP).

---

## Phase 4: User Story 2 - Ler uma imagem só por link temporário e só pelo dono (Priority: P1)

**Goal**: consulta com link temporário, bucket privado, expiração e isolamento entre usuários.

**Independent Test**: Ana envia e lê; o endereço sem assinatura é recusado; o link expira; Bia recebe 404.

### Tests for User Story 2 ⚠️

- [X] T037 [US2] Em `backend/Ambrosio.ApiTests/Controllers/ImagesControllerTests.cs`: `GET /images/{id}` de imagem disponível de A → 200 com `url`, `urlExpiresAt` (≈ agora + `Storage:PresignedReadMinutes`) e metadados; `GET` na `url` sem query string (`StorageHttp.StripQuery`) → 403 do storage; imagem `pending` → 200 com `url = null` e `status = pending`; duas consultas seguidas devolvem `url` diferentes (URL gerada a cada resposta) (US2 cenários 1, 2, 5, 6; critério 3 da HU)
- [X] T038 [US2] Em `ImagesControllerTests.cs`: com o token de B, `GET /images/{idDeA}` → 404 com a mesma `mensagem` de um id inexistente, e `POST /images/uploads/{idDeA}/confirm` → 404 (US2 cenário 4, critério 9 da HU)
- [X] T039 [US2] Em `ImagesControllerTests.cs`, teste lento marcado `[Trait("Category", "Slow")]`: obter a `url`, esperar `PresignedReadMinutes` + 15 s e confirmar 403 do storage; usar o mesmo padrão para o link de envio (`PUT` depois de `PresignedUploadMinutes` + 15 s → 403). Pular com `Skip` dinâmico quando a espera passa de `Storage:SlowTestMaxWaitMinutes` (US2 cenário 3, SC-003, critério 4 da HU, research §16)
- [X] T040 [P] [US2] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/UnauthorizedAccessTests.cs` as cinco rotas de `/images` sem token → 401 (edge case "usuário sem autenticação")

### Implementation for User Story 2

- [X] T041 [US2] Implementar `ImageService.GetByIdAsync` em `backend/Ambrosio.Domain/Services/ImageService.cs`: `GetByIdAsync(imageId, userId)` → `NotFoundException` com a mesma mensagem padrão de inexistente; mapeia para `ImageInfo` (URL só se `Available`)
- [X] T042 [US2] Adicionar `GET /images/{imageId:long}` a `backend/Ambrosio.API/Controllers/ImagesController.cs` (200/404/503, mesmo padrão de `try/catch`) e rodar T037–T040 contra a homologação reconstruída

**Checkpoint**: US1 + US2 entregam envio e leitura privados, a base da HU 010.

---

## Phase 5: User Story 3 - Gravar imagens a partir do próprio servidor (Priority: P2)

**Goal**: `POST /images` multipart, com validação, remoção de EXIF, re-codificação e deduplicação.

**Independent Test**: enviar `small.jpg` (com GPS) e conferir que a imagem baixada não tem EXIF; reenviar e receber a mesma imagem.

### Tests for User Story 3 ⚠️

- [X] T043 [US3] Em `backend/Ambrosio.ApiTests/Controllers/ImagesControllerTests.cs`: `POST /images` multipart com `small.jpg` → 201 `status = available`, `source = server`, `width`/`height` preenchidos; baixar pela `url` e verificar que os bytes **não** contêm o marcador APP1 `Exif\0\0` nem a tag GPS; reenviar o mesmo arquivo → 200 com o mesmo `imageId`; `small.png` e `small.webp` → 201 (US3 cenários 1, 2, 4)
- [X] T044 [US3] Em `ImagesControllerTests.cs`: `POST /images` com `fake.jpg` → 400 (tipo); sem o campo `file` ou com arquivo vazio → 400; arquivo acima de `MaxUploadBytes` (gerado em memória, `MaxUploadBytes + 1` bytes) → 400 ou 413; nos três casos, nenhuma imagem nova (US3 cenário 3)

### Implementation for User Story 3

- [X] T045 [US3] Implementar `ImageService.StoreFromServerAsync` em `backend/Ambrosio.Domain/Services/ImageService.cs`: copia o stream para `MemoryStream` limitado a `MaxUploadBytes` (excedeu → `ImageRejectedException(Size)`); vazio → recusa por tamanho; `InspectAsync` nulo ou fora da lista → recusa por tipo; `SanitizeAsync`; SHA-256 do conteúdo **sanitizado** (FR-010); `GetActiveByHashAsync` → se `Available`, devolve a existente com `Created = false`; senão `StoredImage.CreateAvailable(..., source, now)` + `PutAsync(key, content, mime)` e só então `InsertAsync`; em `DuplicateException`, relê e devolve a existente. Se o `InsertAsync` falhar por outro motivo, apagar o objeto gravado
- [X] T046 [US3] Adicionar `POST /images` a `backend/Ambrosio.API/Controllers/ImagesController.cs`: `[Consumes("multipart/form-data")]`, parâmetro `IFormFile file`, `[RequestSizeLimit]` com `MaxUploadBytes + 64 KB` (ler de `IOptions<ObjectStorageSettings>` num filtro ou usar o valor padrão 10 MB + 64 KB como constante documentada), `ImageSource.Server`, 201/200/400/413/503; arquivo ausente → 400 `ErrorResult` com `erros[].campo = "file"`. Rodar T043–T044 contra a homologação reconstruída

**Checkpoint**: os dois caminhos de entrada da HU existem.

---

## Phase 6: User Story 4 - Excluir uma imagem definitivamente (Priority: P2)

**Goal**: exclusão pelo dono e exclusão em massa por usuário (interna).

**Independent Test**: Ana exclui; a consulta vira 404 e o objeto some; Bia não exclui imagens de Ana.

### Tests for User Story 4 ⚠️

- [X] T047 [US4] Em `backend/Ambrosio.ApiTests/Controllers/ImagesControllerTests.cs`: guardar a `url` de leitura de uma imagem disponível de A; `DELETE /images/{id}` → 204; `GET /images/{id}` → 404; `GET` na `url` guardada (ainda dentro do prazo) → 404 `NoSuchKey` do storage; enviar o mesmo arquivo de novo → 201 como envio novo (US4 cenários 1 e 4, critério 10 da HU, SC-007)
- [X] T048 [US4] Em `ImagesControllerTests.cs`: com o token de B, `DELETE /images/{idDeA}` → 404, e com o token de A a imagem continua disponível e legível; `DELETE` de uma imagem `pending` → 204 (US4 cenário 2)

### Implementation for User Story 4

- [X] T049 [US4] Implementar `ImageService.DeleteAsync` (busca pelo dono → `NotFoundException`; `DeleteAsync(key)` no storage, que ignora inexistente, **antes** de remover o metadado, para nunca sobrar objeto órfão sem registro) e `ImageService.DeleteAllForUserAsync` (`DeletePrefixAsync("ambrosio/users/{userId}/")` + `DeleteAllByUserAsync(userId)`, FR-017, sem rota) em `backend/Ambrosio.Domain/Services/ImageService.cs`
- [X] T050 [US4] Adicionar `DELETE /images/{imageId:long}` a `backend/Ambrosio.API/Controllers/ImagesController.cs` (204/404/503) e rodar T047–T048 contra a homologação reconstruída

**Checkpoint**: ciclo completo de vida da imagem pelo dono.

---

## Phase 7: User Story 5 - Operar os ambientes só com configuração (Priority: P2)

**Goal**: validação no startup, segredos protegidos, produção apontando para o Spaces, CORS e CSP.

**Independent Test**: a API não sobe com bucket vazio nem com prazo fora da faixa; a homologação do zero tem storage pronto; a mesma suíte passa contra MinIO e Spaces.

### Implementation for User Story 5

- [X] T051 [US5] Estender `ValidateSettings` em `backend/Ambrosio.API/Program.cs` com as regras do research §12: `ServiceUrl` (e `PublicServiceUrl`, se informada) URI absoluta http/https; `Region`, `Bucket`, `AccessKey` e `SecretKey` não vazios; `PresignedReadMinutes` 1–60; `PresignedUploadMinutes` 1–15; `MaxUploadBytes` > 0; `AllowedMimeTypes` não vazia e só com `image/jpeg`, `image/png` e `image/webp`; `PendingTtlMinutes` 1–1440; `CleanupIntervalMinutes` 1–60. Cada mensagem nomeia a variável (`ObjectStorage__Bucket está vazio.`) e nunca mostra o valor (FR-020, FR-021)
- [X] T052 [US5] Verificar a validação manualmente, conforme o quickstart §9: `docker compose run --rm -e ObjectStorage__Bucket= api` e `-e ObjectStorage__PresignedReadMinutes=0` falham no startup com a mensagem da variável e sem `AccessKey`/`SecretKey` na saída; registrar o resultado em `specs/004-s3-image-storage/decisions.md` (seção "tasks · validação de startup")
- [X] T053 [P] [US5] Converter `frontend/nginx.conf` em `frontend/nginx/default.conf.template`, acrescentando `${STORAGE_ORIGIN}` a `img-src` e `connect-src` nas quatro ocorrências da CSP (research §15) e preservando as demais diretivas; em `frontend/Dockerfile`, copiar o template para `/etc/nginx/templates/default.conf.template` (o `envsubst` da imagem oficial gera `/etc/nginx/conf.d/default.conf`), restringir o `envsubst` a `STORAGE_ORIGIN` com `NGINX_ENVSUBST_FILTER` para não substituir as variáveis do próprio nginx (`$uri` etc.) e definir `ENV STORAGE_ORIGIN=` como padrão
- [X] T054 [US5] Em `docker-compose.yml`, definir `STORAGE_ORIGIN: http://localhost:${STORAGE_PORT:-9000}` no serviço `frontend` (depende de T053); em `docker-compose-prod.yml`, `STORAGE_ORIGIN: ${OBJECT_STORAGE_PUBLIC_ORIGIN}` no `frontend` e, no anchor `x-api-environment`, `ObjectStorage__AccessKey: ${OBJECT_STORAGE_ACCESS_KEY}` e `ObjectStorage__SecretKey: ${OBJECT_STORAGE_SECRET_KEY}` (skill `dotnet-env`)
- [X] T055 [P] [US5] Acrescentar a `.env.prod.example` a seção "Storage de imagens (DigitalOcean Spaces)" com `OBJECT_STORAGE_ACCESS_KEY=<SPACES_KEY_COM_ESCOPO_NO_BUCKET>`, `OBJECT_STORAGE_SECRET_KEY=<SPACES_SECRET>` e `OBJECT_STORAGE_PUBLIC_ORIGIN=https://ambrosia-prod.<região>.digitaloceanspaces.com`; em `.github/workflows/deploy-prod.yml`, gravar as três variáveis no `.env.prod` a partir dos GitHub Secrets de mesmo nome, no mesmo passo das já existentes
- [X] T056 [P] [US5] Em `deploy/README.md`, adicionar a seção "Storage de imagens": criar o bucket `ambrosia-prod` privado no Spaces (região escolhida), criar a chave com escopo só nesse bucket, aplicar a regra CORS (`AllowedOrigins https://ambrosiaai.site`, métodos `GET, PUT, HEAD`, cabeçalho `Content-Type`, `MaxAgeSeconds 3600`; research §14), cadastrar os três GitHub Secrets e ajustar `ServiceUrl`/`Region` em `appsettings.Production.json`. Destacar que a API **não sobe** em produção sem esses itens (plan.md, Complexity Tracking)
- [X] T057 [US5] Em `backend/Ambrosio.ApiTests/Controllers/ImagesControllerTests.cs`, teste de CORS: `OPTIONS` na `uploadUrl` com `Origin: http://localhost:8080` e `Access-Control-Request-Method: PUT` → resposta com `Access-Control-Allow-Origin` igual à origem; a origem esperada vem de `Storage:FrontendOrigin` em `backend/Ambrosio.ApiTests/appsettings.Test.json` (FR-022, critério 12 da HU)
- [ ] T058 [US5] Reconstruir a homologação do zero (`docker compose --profile frontend down -v` e `up -d --build`) e verificar sem nenhum passo manual: buckets criados, bucket privado, cabeçalho CSP em `http://localhost:8080` contendo `http://localhost:9000` em `img-src` e `connect-src` (SC-009, FR-023); rodar a suíte completa
- [ ] T059 [US5] Rodar a suíte `ImagesControllerTests` contra um bucket de teste no Spaces, com uma API local apontada para ele só por variáveis `ObjectStorage__*` (os testes lentos são pulados pelos prazos de produção), e registrar em `specs/004-s3-image-storage/decisions.md` (seção "tasks · SC-001") o resultado e qualquer incompatibilidade de checksum ou de assinatura. Se ainda não houver conta no provedor, registrar a pendência como bloqueio do deploy em produção

**Checkpoint**: ambientes reproduzíveis e produção pronta para receber as credenciais.

---

## Phase 8: User Story 6 - Limpar envios abandonados (Priority: P3)

**Goal**: job periódico que remove pendentes e falhas com mais de `PendingTtlMinutes`.

**Independent Test**: declarar sem confirmar e, depois do prazo de homolog (2 min + 1 min de intervalo), a imagem não existe mais.

### Tests for User Story 6 ⚠️

- [X] T060 [US6] Em `backend/Ambrosio.ApiTests/Controllers/ImagesControllerTests.cs`, teste lento `[Trait("Category", "Slow")]`: declarar e enviar sem confirmar (pendente com objeto), declarar sem enviar (pendente sem objeto), provocar uma falha (hash divergente) e ter uma imagem disponível; esperar `PendingTtlMinutes + CleanupIntervalMinutes` + 30 s; os três primeiros → `GET` 404, e a disponível continua 200; pular quando a espera passa de `Storage:SlowTestMaxWaitMinutes` (US6 cenários 1, 3 e 4, SC-011). O cenário 2 (pendente recente preservada) é coberto pelos testes da US1 que confirmam logo após declarar

### Implementation for User Story 6

- [X] T061 [US6] Implementar `ImageService.PurgeStaleAsync(now)` em `backend/Ambrosio.Domain/Services/ImageService.cs` (research §8, §9): `ListStaleAsync(now - PendingTtl, now - PendingTtl, take: 200)` em laço até esvaziar; pendente → `DeleteAsync(key)` no storage e depois remove o metadado; falha → só remove o metadado (o objeto já foi apagado e a chave pode estar em uso); devolve o total removido e loga a contagem
- [X] T062 [US6] Criar `backend/Ambrosio.Application/HostedServices/PendingImageCleanupService.cs` (`BackgroundService` com `PeriodicTimer` de `CleanupIntervalMinutes`, escopo novo por ciclo via `IServiceScopeFactory`, chamada a `IImageService.PurgeStaleAsync(clock.UtcNow)`, `catch` de `StorageUnavailableException`/`Exception` com log sem detalhes sensíveis, para não derrubar o host) e registrá-lo com `services.AddHostedService<PendingImageCleanupService>()` em `backend/Ambrosio.Application/Startup.cs`; rodar T060 contra a homologação reconstruída

**Checkpoint**: todas as histórias entregues.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T063 [P] Atualizar o contrato consolidado `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml` com as cinco rotas `/images`, os schemas `ImageUploadInsertInfo`, `ImageUploadResult`, `ImageInfo`, `ImageStatus`, `ImageSource` e a resposta 503, conforme `contracts/images-api.md`, e conferir no Swagger da homologação que nomes de schema e parâmetros batem (o `ContractAlignmentOperationFilter` já aplica o Bearer por operação)
- [X] T064 [P] Criar a pasta `bruno/Images/` com `Declarar envio.bru` (grava `imageId` e `uploadUrl` nas variáveis), `Enviar arquivo (PUT).bru` (usa `{{uploadUrl}}` e `Content-Type: image/jpeg`), `Confirmar envio.bru`, `Gravar pelo servidor.bru` (multipart), `Consultar imagem.bru` e `Excluir imagem.bru`, no padrão das requisições existentes (token de `Login com senha`)
- [X] T065 [P] Atualizar `backend/README.md` (seção "Storage de imagens": endpoints, configuração `ObjectStorage__*`, Development usando o MinIO da homolog na porta 9000 com o bucket `ambrosio-dev`) e a tabela de variáveis em `docs/README.md`, se houver
- [X] T066 [P] Atualizar `docs/historias/003-armazenamento-imagens-s3.md`: campo `Spec` → `specs/004-s3-image-storage`, `Status` → "Em implementação"/"Concluída" ao fim, `Last Updated`, e registrar as decisões das clarificações (EXIF só no caminho do servidor, sem cota, falhas sem deduplicação, Development usando o MinIO da homolog) na seção "Perguntas em aberto" como resolvidas
- [X] T067 Revisão de segurança (FR-013, FR-021): procurar em `backend/` por `Log*(` próximos de `uploadUrl`, `Url`, `AccessKey`, `SecretKey` e `ServiceUrl`; conferir que nenhuma exceção relançada pela Infra inclui endpoint ou bucket no `Message`; conferir nos logs do container `api` depois da suíte (`docker compose logs api`) que não aparece `X-Amz-Signature` nem a chave secreta; registrar o resultado em `specs/004-s3-image-storage/decisions.md` (seção "tasks · revisão de segurança")
- [X] T068 Registrar em `specs/004-s3-image-storage/decisions.md` os desvios aceitos: CORS por servidor do MinIO nos ambientes locais (research §14), leitura sem `userId` em `ListStaleAsync` (§VI) e o pré-requisito do provedor para o deploy em produção
- [ ] T069 Fechamento do ciclo (constituição §VII): reconstruir a homologação do zero, rodar a suíte completa `dotnet test backend/Ambrosio.ApiTests` (incluindo os testes `Slow`) com todos verdes, e seguir o `quickstart.md` de ponta a ponta, anotando divergências em `specs/004-s3-image-storage/decisions.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências; T004 depende de T003 (versão do ImageSharp).
- **Foundational (Phase 2)**: depende do Setup; **bloqueia todas as histórias**. Ordem interna: T005 → T006; T008 → T009 → T011; T012 → T015 → T016 → T017; T019 → T020/T021/T022 → T023; T024 → T026.
- **US1 (Phase 3)**: depende da Phase 2. É o MVP.
- **US2 (Phase 4)**: depende da Phase 2. Os testes reutilizam o helper de envio da US1 (T029), então na prática vem depois da US1.
- **US3 (Phase 5)**: depende da Phase 2; independente da US1/US2 na implementação (os testes usam só `POST /images` e `GET` da `url` devolvida).
- **US4 (Phase 6)**: depende da Phase 2; os testes usam envio (US1 ou US3) e `GET /images/{id}` (US2).
- **US5 (Phase 7)**: T051–T056 dependem só da Phase 2; T057–T059 dependem de US1 (para gerar `uploadUrl`) e da suíte existente.
- **US6 (Phase 8)**: depende da Phase 2; o teste usa envio e confirmação da US1.
- **Polish (Phase 9)**: depois das histórias desejadas.

### User Story Dependencies

```text
Phase 1 → Phase 2 ─┬─ US1 (MVP) ─┬─ US2 ─┬─ US4
                   │             │       └─ US5 (T057–T059)
                   │             └─ US6
                   ├─ US3 (implementação independente)
                   └─ US5 (T051–T056, independentes)
                                         → Phase 9
```

### Within Each User Story

- Testes primeiro (falhando), depois validator/serviço, depois controller, depois rodar contra a homologação reconstruída.
- Todos os métodos do `ImageService` ficam no mesmo arquivo: tarefas de serviço de histórias diferentes **não** são paralelas entre si.
- `ImagesControllerTests.cs` também é arquivo único: tarefas de teste de histórias diferentes são sequenciais.

### Parallel Opportunities

- Phase 1: T002 e T003 em paralelo.
- Phase 2: T005, T007, T010, T012, T013, T014, T018, T019, T025, T027 e T028 em paralelo (arquivos distintos); depois T020, T021 e T022 em paralelo entre si.
- US5: T053, T055 e T056 em paralelo com qualquer história.
- Polish: T063–T066 em paralelo.

---

## Parallel Example: Phase 2

```text
# Arquivos distintos, sem dependência entre si:
Task: "T005 Criar ObjectStorageSettings em backend/Ambrosio.DTO/Settings/ObjectStorageSettings.cs"
Task: "T012 Criar ImageStatus/ImageSource em backend/Ambrosio.DTO/Enums/"
Task: "T013 Criar DTOs de API em backend/Ambrosio.DTO/Images/"
Task: "T018 Criar ImageRejectedException/StorageUnavailableException em backend/Ambrosio.Domain/Exceptions/"
Task: "T028 Adicionar arquivos de teste em backend/Ambrosio.ApiTests/Fixtures/Images/"

# Depois de T019:
Task: "T020 ObjectStorageAppService em backend/Ambrosio.Infra/AppServices/"
Task: "T021 ImageProcessingAppService em backend/Ambrosio.Infra/AppServices/"
Task: "T022 StoredImageRepository em backend/Ambrosio.Infra/Repository/"
```

## Parallel Example: User Story 5

```text
Task: "T053 Template da CSP em frontend/nginx/default.conf.template + frontend/Dockerfile"
Task: "T055 .env.prod.example + .github/workflows/deploy-prod.yml"
Task: "T056 Seção de storage em deploy/README.md"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 (Setup) e Phase 2 (Foundational).
2. Phase 3 (US1): envio pelo navegador com confirmação.
3. **Parar e validar**: T029–T031 verdes contra a homologação.
4. Em seguida, a US2 completa o par mínimo (envio + leitura) que a HU 010 precisa.

### Incremental Delivery

1. Base → US1 → US2: storage privado utilizável (envio e leitura).
2. US3: gravação pelo servidor (desbloqueia a 010 para fotos baixadas).
3. US4: exclusão (LGPD).
4. US5: produção configurável. **Antes do merge para `main`**, o bucket e os secrets do provedor precisam existir (T056, T059).
5. US6: limpeza.
6. Phase 9: contrato, Bruno, docs e fechamento do ciclo (T069).

### Notes

- [P] = arquivos diferentes e sem dependência pendente.
- Toda chamada `docker`/`docker compose` usa a skill `wsl-docker`.
- Nenhum teste unitário do backend (constituição §VIII); nenhuma alteração em `backend/Ambrosio.Tests`.
- Commit ao fim de cada fase ou checkpoint.
