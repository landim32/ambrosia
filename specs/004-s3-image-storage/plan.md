# Implementation Plan: Armazenamento de imagens em storage compatível com S3

**Branch**: `004-s3-image-storage` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/004-s3-image-storage/spec.md` (HU `docs/historias/003-armazenamento-imagens-s3.md`)

## Summary

Criar a infraestrutura única de imagens do Ambrosio: binários num storage **compatível com S3 e privado**, metadados na nova tabela `stored_images`, e acesso só por **URLs pré-assinadas** geradas a cada resposta. A Infra ganha `ObjectStorageAppService` (`AWSSDK.S3` 4.x com `ServiceURL`/`ForcePathStyle` configuráveis, checksums `WHEN_REQUIRED`, endpoint público separado só para assinar URLs) e `ImageProcessingAppService` (ImageSharp 3.1: tipo pelo conteúdo, dimensões, remoção de EXIF). O Domain ganha `ImageService`, com os fluxos de envio pelo cliente (declarar → `PUT` direto no storage → confirmar com `HeadObject` + download + SHA-256 + *magic bytes*), gravação pelo servidor, consulta, exclusão e limpeza. A API expõe cinco rotas autenticadas em `/images` (clarificação 1) e um `BackgroundService` limpa pendentes e falhas antigas. A homologação sobe MinIO (`storage`) + `storage-init` (buckets `ambrosio-homolog` e `ambrosio-dev`, privados, CORS). Development usa esse MinIO pela porta do host (clarificação 3). Produção aponta para o DigitalOcean Spaces só por configuração. A CSP do frontend passa a incluir o endpoint do storage via template do nginx. Os testes de API cobrem os 12 critérios da HU.

Decisões detalhadas em [research.md](./research.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (backend); nginx 1.27 (frontend estático, só CSP muda)

**Primary Dependencies**:
- Backend (existentes): ASP.NET Core 8, EF Core 9.x + Npgsql, FluentValidation 11, Swashbuckle 8
- Backend (**novos**): `AWSSDK.S3` 4.x (Infra), `SixLabors.ImageSharp` 3.1.x (Infra), `Microsoft.Extensions.Hosting.Abstractions` 9.0.x (Application, para o `BackgroundService`)
- Homolog: imagens `minio/minio` e `minio/mc` com tag `RELEASE.*` fixa
- Testes de API: xUnit + Flurl.Http + FluentAssertions (`Ambrosio.ApiTests`)

**Storage**: PostgreSQL (nova tabela `stored_images`, migração `AddStoredImages`); storage de objetos S3 compatível (MinIO em homolog/dev, Spaces em produção), chave `ambrosio/users/{userId}/images/{sha256}.{ext}`

**Testing**: `Ambrosio.ApiTests` contra Homolog (`http://localhost:8080/api`) com MinIO, e uma vez contra um bucket de teste no Spaces (SC-001). Nenhum teste unitário novo no backend (§VIII). O frontend não muda de código (só a configuração do nginx).

**Target Platform**: API Linux em container atrás do gateway nginx (`/api`); storage acessado diretamente pelo navegador via URL assinada

**Project Type**: web application (`backend/` + `frontend/`), mais compose e deploy

**Performance Goals**: envio de 10 MB do pedido à confirmação em < 10 s (SC-010). A confirmação baixa no máximo `MaxUploadBytes` e só depois do `HeadObject` aprovar o tamanho. Gerar URL pré-assinada é operação local (sem rede).

**Constraints**:
- Bucket privado; nenhuma ACL pública (FR-004); CORS só para a origem do frontend (FR-022)
- URLs nunca persistidas nem logadas por completo; credenciais nunca em log, erro ou Swagger (FR-013, FR-021)
- Toda leitura filtra por `user_id` do `sub`; imagem de outro usuário → 404 (FR-014, §VI)
- API não sobe com `ObjectStorage` incompleta ou fora das faixas (FR-020)
- Sem cota por usuário (clarificação 4); sem EXIF no caminho do servidor, EXIF preservado no envio direto do navegador (clarificação 2)
- Trocar MinIO ↔ Spaces só por configuração (FR-002)

**Scale/Scope**: 1 tabela + 1 migração; 1 entidade, 2 enums, 4 DTOs de API + 4 tipos de transporte, 1 validator; 2 AppServices + 1 repositório + 1 serviço de domínio + 2 exceções; 1 controller com 5 rotas; 1 `BackgroundService`; ~25 testes de API; 2 serviços novos no compose de homolog; variáveis e segredos novos em homolog/prod; CSP do frontend parametrizada

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Princípio | Como o plano atende | Status |
|---|---|---|---|
| I | Skills obrigatórias | `dotnet-architecture` para entidade, repositório, AppServices, DTOs, Fluent API, migração e DI no `Startup`. `dotnet-fluent-validation` para `ImageUploadInsertInfoValidator`. `dotnet-env` para appsettings, `.env.example`, `.env.prod.example`, compose e `deploy-prod.yml`. `docker-compose-config` + `wsl-docker` para os serviços `storage`/`storage-init`. `dotnet-test-api` para `ImagesControllerTests`. `react-architecture` e `dotnet-graphql` não se aplicam (sem código React nem GraphQL). | ✅ |
| II | Stack fixa | Nenhuma tecnologia proibida. EF Core continua o único ORM. Os pacotes novos (`AWSSDK.S3`, `ImageSharp`, `Hosting.Abstractions`) não substituem nada da stack fixa e ficam restritos a Infra/Application. Docker local pela `wsl-docker`. | ✅ |
| III | Case sensitivity | Sem arquivos novos no frontend além do template do nginx; pastas do backend em PascalCase (`Images/`, `HostedServices/`). | ✅ |
| IV | Convenções de código | DTOs `ImageUploadInsertInfo` / `ImageUploadResult` / `ImageInfo` com `[JsonPropertyName]`; enums como string camelCase (conversor global já existente); `ErrorResult` em português; namespaces file-scoped; constantes `UPPER_CASE`. | ✅ |
| V | Banco de dados | `stored_images` em snake_case plural, PK `stored_image_id` bigint identity, `stored_images_pkey`, FK `fk_user_stored_image` com `ClientSetNull`, `timestamp without time zone`, `varchar` com tamanho, `status`/`source` `integer` com default. Sem cascata: a exclusão é explícita no serviço. | ✅ |
| VI | Autenticação e segurança | `ImagesController` com `[Authorize]`, sem `[AllowAnonymous]`. `userId` só do `sub` (nenhum DTO carrega dono). Repositório filtra por `userId` como primeira cláusula; outro usuário → 404. Segredos do storage vêm do ambiente, com validação no startup. **Exceção justificada**: `ListStaleAsync` (limpeza) lê sem `userId`, usada só pelo `BackgroundService` e nunca por endpoint (ver Complexity Tracking). | ✅ (1 exceção justificada) |
| VII | Ambientes e testes de API | Os três ambientes pela `dotnet-env`. Homolog sobe tudo no compose, inclusive storage e buckets (SC-009). Development continua **sem Docker para a API** e só consome o storage da homologação pela porta do host (clarificação 3, sem compose próprio de dev). Os endpoints novos ganham testes no mesmo ciclo; a homologação é reconstruída e a suíte roda contra ela. | ✅ |
| VIII | Estratégia de testes | Backend só com testes de API. Nenhum teste unitário criado ou ajustado em `Ambrosio.Tests` (desvio pré-existente, não ampliado). Frontend sem mudança de código e sem E2E. | ✅ |
| — | Variáveis de ambiente | Novas: `ObjectStorage__*` no backend (tabela no research §12); `STORAGE_*` no `.env` de homolog; `OBJECT_STORAGE_ACCESS_KEY` / `OBJECT_STORAGE_SECRET_KEY` nos GitHub Secrets; `STORAGE_ORIGIN` no container do frontend (configuração do nginx, não é `VITE_*` porque não entra no bundle). | ✅ |
| — | Tratamento de erros | `try/catch` nos controllers no padrão existente: `ValidationException` → 400, `ImageRejectedException` → 400, `NotFoundException` → 404, `StorageUnavailableException` → 503 (antes do `catch (Exception) → 500`). A Infra nunca coloca endpoint, bucket ou chave no `Message` das exceções relançadas, para o `ex.Message` do 500 não vazar dados (FR-021). | ✅ |

**Resultado do gate (pré-Phase 0)**: APROVADO, com uma exceção justificada (§VI, leitura sem `userId` restrita ao job de limpeza).

**Re-avaliação pós-Phase 1**: data-model (tabela e índices parciais em conformidade com §V), contrato (5 rotas `[Authorize]`, 404 para outro dono, sem `userId` em DTO) e quickstart (homolog reconstruída + ApiTests) mantêm todas as regras. O único desvio de spec registrado é o CORS por servidor do MinIO nos ambientes locais (research §14, ver Complexity Tracking). APROVADO.

## Project Structure

### Documentation (this feature)

```text
specs/004-s3-image-storage/
├── plan.md                 # Este arquivo
├── spec.md                 # Especificação funcional (com Clarifications 2026-10-26)
├── research.md             # Phase 0: decisões técnicas
├── data-model.md           # Phase 1: tabela, entidade, estados, DTOs, interfaces
├── quickstart.md           # Phase 1: validação ponta a ponta
├── contracts/
│   └── images-api.md       # Rotas /images, códigos, comportamento do storage
├── checklists/requirements.md
└── tasks.md                # Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── Ambrosio.DTO/
│   ├── Enums/ImageStatus.cs, Enums/ImageSource.cs                     # NOVOS
│   ├── Images/ImageUploadInsertInfo.cs, ImageUploadResult.cs, ImageInfo.cs   # NOVOS (API)
│   ├── Images/ObjectHead.cs, PresignedUrl.cs, ImageInspection.cs, ProcessedImage.cs   # NOVOS (transporte)
│   └── Settings/ObjectStorageSettings.cs                              # NOVO
├── Ambrosio.Domain/
│   ├── Models/StoredImage.cs                                          # NOVO (+ regra de chave e extensões)
│   ├── Interfaces/IImageService.cs                                    # NOVO
│   ├── Services/ImageService.cs                                       # NOVO
│   ├── Exceptions/ImageRejectedException.cs, StorageUnavailableException.cs   # NOVOS
│   └── Validators/ImageUploadInsertInfoValidator.cs                   # NOVO (usa IOptions<ObjectStorageSettings>)
├── Ambrosio.Infra.Interfaces/
│   ├── AppServices/IObjectStorageAppService.cs, IImageProcessingAppService.cs   # NOVOS
│   └── Repository/IStoredImageRepository.cs                           # NOVO
├── Ambrosio.Infra/
│   ├── Ambrosio.Infra.csproj                                          # + AWSSDK.S3, SixLabors.ImageSharp
│   ├── AppServices/ObjectStorageAppService.cs                         # NOVO (cliente interno + cliente de assinatura)
│   ├── AppServices/ImageProcessingAppService.cs                       # NOVO
│   ├── Repository/StoredImageRepository.cs                            # NOVO
│   ├── Context/AmbrosioContext.cs                                     # + DbSet<StoredImage> e Fluent API
│   └── Migrations/*_AddStoredImages.cs                                # NOVO (dotnet ef)
├── Ambrosio.Application/
│   ├── Ambrosio.Application.csproj                                    # + Microsoft.Extensions.Hosting.Abstractions
│   ├── Settings/AmbrosioSettings.cs                                   # + ObjectStorage
│   ├── HostedServices/PendingImageCleanupService.cs                   # NOVO
│   └── Startup.cs                                                     # registra settings, repo, AppServices, ImageService, hosted service
├── Ambrosio.API/
│   ├── Controllers/ImagesController.cs                                # NOVO: 5 rotas [Authorize]
│   ├── Controllers/ApiControllerBase.cs                               # + ImageRejectedError (400), StorageUnavailableError (503)
│   ├── Program.cs                                                     # lê/valida ObjectStorage; descrição do Swagger
│   └── appsettings*.json                                              # seção ObjectStorage por ambiente (sem segredos fora de Development)
├── Ambrosio.ApiTests/
│   ├── Controllers/ImagesControllerTests.cs                           # NOVO
│   ├── Controllers/UnauthorizedAccessTests.cs                         # + rotas /images
│   ├── Helpers/StorageHttp.cs                                         # NOVO: PUT/GET diretos no storage, SHA-256
│   ├── Fixtures/Images/*.jpg|png|webp|pdf.jpg                         # NOVOS (arquivos de teste; csproj copia para a saída)
│   └── appsettings.Test.json                                          # + Storage:* (prazos esperados)
└── README.md                                                          # seção de storage

docker-compose.yml                  # + storage (MinIO), storage-init (mc); api depende de storage-init; env ObjectStorage__*; frontend recebe STORAGE_ORIGIN
docker-compose-prod.yml             # env ObjectStorage__AccessKey/SecretKey; frontend recebe STORAGE_ORIGIN
.env.example / .env.prod.example    # STORAGE_* (homolog) / OBJECT_STORAGE_* (prod)
.github/workflows/deploy-prod.yml   # grava OBJECT_STORAGE_* no .env.prod
deploy/README.md                    # criar bucket/chave no Spaces, regra CORS, região
frontend/nginx.conf → frontend/nginx/default.conf.template   # CSP com ${STORAGE_ORIGIN} em img-src e connect-src
frontend/Dockerfile                 # copia o template para /etc/nginx/templates/
bruno/Images/*.bru                  # NOVOS: declarar envio, confirmar, gravar, consultar, excluir
docs/historias/003-armazenamento-imagens-s3.md   # Spec → 004-s3-image-storage; status
specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml   # + /images (contrato consolidado)
```

**Structure Decision**: aplicação web existente, sem projetos novos. O storage e o processamento de imagem são **AppServices da Infra** atrás de interfaces em `Infra.Interfaces`, como `CpfProtectionAppService` e `PasswordHashAppService`. Assim o Domain não conhece AWS nem ImageSharp, e trocar de provedor ou de biblioteca não toca as regras. As regras (deduplicação, transições de status, conferência da confirmação, limpeza) ficam no `ImageService` do Domain. O `BackgroundService` fica na camada Application, porque o composition root (`Startup`) concentra o DI, e só abre um escopo e chama o `IImageService`. As HUs consumidoras (010, 004) vão reutilizar `IImageService` e `ImageInfo` sem novas abstrações.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| §VI: `IStoredImageRepository.ListStaleAsync` lê sem `userId` | O job de limpeza (FR-018) varre pendentes e falhas de **todos** os usuários | Iterar por usuário multiplicaria consultas sem ganho de isolamento: o job não atende requisição de usuário e não expõe dados. O método não é usado por nenhum endpoint, o que a revisão verifica. |
| Desvio da FR-022 nos ambientes locais: CORS por servidor no MinIO (os dois buckets locais aceitam `localhost:8080` e `localhost:5173`) | O MinIO não aplica CORS por bucket | Um container de MinIO por ambiente local duplicaria serviço e portas. Os dados locais são descartáveis, e em produção a regra é por bucket e só para `https://ambrosiaai.site`. Registrar em `decisions.md`. |
| Endpoint público separado para assinar URLs (`PublicServiceUrl`) | Em homolog, a API alcança o storage por `storage:9000`, mas navegador e testes só por `localhost:9000`, e a assinatura inclui o host | Publicar o storage atrás do gateway quebra a assinatura em path-style; DNS no host do Windows/WSL é frágil. |
| Dependência operacional de produção | A API passa a recusar o startup sem `ObjectStorage` completo, então o deploy em produção exige bucket, chave com escopo no bucket, regra CORS e os dois GitHub Secrets **antes** do merge | Deixar o storage opcional em produção violaria FR-020. O `deploy/README.md` ganha o checklist do provedor. |
