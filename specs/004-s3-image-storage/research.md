# Research: Armazenamento de imagens em storage compatível com S3

**Feature**: `004-s3-image-storage` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

Cada seção registra **Decision / Rationale / Alternatives considered**. Itens marcados como "verificar na implementação" são fatos de versão de terceiros que precisam ser conferidos ao instalar o pacote ou a imagem, sem mudar a decisão.

---

## §1 SDK de storage

**Decision**: `AWSSDK.S3` **4.x** (pacote oficial da AWS, sem SDK de provedor), configurado por `AmazonS3Config { ServiceURL, ForcePathStyle, AuthenticationRegion }` e credenciais `BasicAWSCredentials(AccessKey, SecretKey)`. Usado só na camada Infra (`ObjectStorageAppService`).

**Rationale**: é o cliente S3 de referência, funciona com Spaces, MinIO e qualquer serviço compatível apenas trocando `ServiceURL` (FR-002, SC-001). A versão 4 é a linha atual mantida.

**Alternatives considered**:
- SDK/API específicos do DigitalOcean: amarra ao provedor (fere FR-002).
- Cliente `Minio` (.NET): funciona com S3 genérico, mas é menos usado contra Spaces e duplicaria o risco de compatibilidade.
- HTTP + SigV4 manual: reimplementar assinatura é risco de segurança sem ganho.

## §2 Checksums automáticos do SDK

**Decision**: `RequestChecksumCalculation = WHEN_REQUIRED` e `ResponseChecksumValidation = WHEN_REQUIRED` no `AmazonS3Config`. A suíte de testes de API roda contra MinIO (homolog) e, antes do primeiro deploy, contra um bucket de homologação no Spaces (SC-001).

**Rationale**: as versões recentes do SDK enviam cabeçalhos `x-amz-checksum-*` / `x-amz-sdk-checksum-algorithm` por padrão (*flexible checksums*), que alguns serviços compatíveis recusam ou não validam. `WHEN_REQUIRED` mantém o checksum só onde a API S3 exige (ex.: `DeleteObjects`).

**Alternatives considered**: manter o padrão `WHEN_SUPPORTED` — quebra uploads em provedores que não implementam os algoritmos novos.

## §3 Dois endpoints: interno e público (assinatura das URLs)

**Decision**: duas configurações de endereço:
- `ObjectStorage:ServiceUrl` — usado pela API para operações (`Head`, `Get`, `Put`, `Delete`, `List`).
- `ObjectStorage:PublicServiceUrl` — opcional (padrão = `ServiceUrl`), usado **só para gerar URLs pré-assinadas** que o navegador e os testes vão chamar.

O `ObjectStorageAppService` mantém dois `IAmazonS3`: um para operações e outro (sem chamadas de rede) para pré-assinar.

**Rationale**: a assinatura SigV4 inclui o `Host`. Em homolog a API fala com `http://storage:9000` (rede do compose), mas o navegador e a suíte de testes só alcançam `http://localhost:9000`; uma URL assinada para `storage:9000` seria inútil, e reescrever o host invalida a assinatura. Em Development e Production os dois valores coincidem.

**Alternatives considered**:
- Publicar o MinIO por trás do gateway em `/storage`: o prefixo de caminho também quebra a assinatura em path-style.
- `extra_hosts`/DNS para fazer `storage` resolver no host: frágil no Windows/WSL.

## §4 URL pré-assinada de envio (PUT)

**Decision**: `GetPreSignedURLAsync` com `Verb = PUT`, `ContentType = <mime declarado>` (vira cabeçalho assinado), `Expires = agora + PresignedUploadMinutes` e `Protocol` derivado do esquema do `PublicServiceUrl` (HTTP no MinIO local). A resposta da API devolve `uploadUrl`, `uploadMethod: "PUT"`, `contentType` e `uploadExpiresAt`; o cliente DEVE enviar exatamente esse `Content-Type`.

**Rationale**: com o `Content-Type` assinado, um `PUT` com outro tipo responde 403 no storage (US1 cenário 2). O PUT pré-assinado **não** limita o tamanho do corpo; isso é compensado pela confirmação obrigatória (§6) e pela limpeza (§9), como prevê a HU.

**Alternatives considered**:
- POST policy (`content-length-range`): limita o tamanho no storage, mas o suporte é irregular entre provedores compatíveis (risco listado na HU) e exige formulário multipart no navegador.
- `x-amz-checksum-sha256` assinado no PUT: faria o storage recusar hash divergente, mas o suporte do Spaces é pergunta em aberto da HU. Fica como reforço futuro; a conferência do hash é feita pela API (§6), o que vale para qualquer provedor.

## §5 URL pré-assinada de leitura (GET)

**Decision**: `GetPreSignedURLAsync` com `Verb = GET`, `Expires = agora + PresignedReadMinutes`, gerada a cada resposta que contém uma imagem disponível. Nunca persistida; em log aparece no máximo o `imageId`.

**Rationale**: FR-012/FR-013. O bucket é privado; sem a query string assinada o storage responde 403 (US2 cenário 2).

## §6 Confirmação do envio pelo cliente

**Decision**: `POST /images/uploads/{imageId}/confirm`:
1. Carrega a imagem do usuário (404 se não for dele). `available` → devolve como está, com URL nova (idempotente). `failed` → 400.
2. `HeadObject`: inexistente → 400 "arquivo ainda não enviado" (continua `pending`). `ContentLength` ≠ declarado ou > `MaxUploadBytes` → falha.
3. `GetObject` em streaming para um `MemoryStream` limitado a `MaxUploadBytes` (no máximo 10 MB por padrão), calculando o SHA-256 em paralelo.
4. Detecção do formato pelo conteúdo (§7) e comparação com o mime declarado e com a lista aceita; leitura das dimensões pelo cabeçalho.
5. Tudo confere → `available` + dimensões. Algo diverge → `DeleteObject`, `failed`, 400 com o motivo (`tamanho`, `tipo` ou `hash`).

**Rationale**: o hash só pode ser verificado lendo o conteúdo inteiro; o limite de tamanho é conferido no `HeadObject` **antes** do download, então um objeto gigante nunca é baixado.

**Alternatives considered**: ler só os primeiros bytes (proposta original da HU) — confere o tipo, mas não o hash (critério 6 da HU exige hash).

## §7 Processamento de imagem (tipo pelo conteúdo, dimensões, EXIF)

**Decision**: **SixLabors.ImageSharp 3.1.x** na Infra, atrás de `IImageProcessingAppService`:
- `Image.DetectFormatAsync` (assinatura de bytes) → tipo real; formato desconhecido (ex.: PDF renomeado) = recusado.
- `Image.IdentifyAsync` → largura/altura sem decodificar a imagem inteira.
- Gravação pelo servidor: decodifica, aplica `AutoOrient()` (para a foto não "girar" ao perder a tag de orientação), remove os perfis EXIF, IPTC e XMP (mantém ICC para não alterar cores) e re-codifica no mesmo formato (JPEG qualidade 90, PNG, WebP qualidade 90).

**Rationale**: um pacote gerenciado (sem binário nativo no container Linux) cobre os três formatos para detecção, dimensões e remoção de metadados. A re-codificação também neutraliza arquivos "poliglotas" (imagem válida com conteúdo extra).

**Alternatives considered**:
- Parser próprio de segmentos JPEG/PNG/WebP: sem dependência, mas exige tratar VP8X do WebP, orientação e casos de borda — mais código de risco.
- SkiaSharp: exige assets nativos no container.
- Magick.NET: binário nativo pesado.

**Nota de licença**: ImageSharp 3.x usa a *Six Labors Split License* (gratuita para projetos open source e para empresas com receita anual abaixo do limite da licença). Registrar em `decisions.md` a verificação da elegibilidade antes do deploy em produção. Verificar na implementação a última 3.1.x estável.

## §8 Deduplicação e concorrência

**Decision**: índice único **parcial** `(user_id, sha256) WHERE status IN (1, 2)` e índice único parcial em `storage_key` com o mesmo filtro. Fluxo de criação: consulta prévia → insere `pending` → em violação de unicidade (`23505`), relê e devolve o registro existente.

**Rationale**: FR-011 com a clarificação 5: imagens `failed` não participam da unicidade, então um reenvio do mesmo conteúdo cria um registro novo com a **mesma** chave de objeto (o objeto da tentativa falha já foi apagado). O índice parcial resolve a corrida de dois pedidos simultâneos (edge case).

**Consequência**: a limpeza de registros `failed` **não** apaga objeto (ele foi apagado na recusa e a chave pode estar em uso por um envio novo); só a limpeza de `pending` apaga objeto.

## §9 Limpeza de pendentes e falhas

**Decision**: `PendingImageCleanupService : BackgroundService` (em `Ambrosio.Application/HostedServices`, registrado no `Startup`), com `PeriodicTimer` de `CleanupIntervalMinutes` (padrão 15). A cada ciclo, cria um escopo e chama `IImageService.PurgeStaleAsync(now)`:
- `pending` com `created_at < now − PendingTtlMinutes` → `DeleteObject` (ignora inexistente) + remove o metadado.
- `failed` com `updated_at < now − PendingTtlMinutes` → remove só o metadado.

Falhas de storage num ciclo são registradas em log (sem URL nem credencial) e tentadas no próximo.

**Rationale**: FR-018 sem infraestrutura extra de agendamento. `PendingTtlMinutes` (padrão 60, faixa 1–1440) e o intervalo (faixa 1–60) são configuráveis para que a homologação use valores curtos e o teste de API da US6 rode em poucos minutos.

**Alternatives considered**: regra de ciclo de vida do bucket (lifecycle) — não remove o metadado no banco e o suporte varia entre provedores; Hangfire/Quartz — dependência nova para um único job.

## §10 Exclusão de todas as imagens de um usuário

**Decision**: `IObjectStorageAppService.DeletePrefixAsync("ambrosio/users/{userId}/")` com `ListObjectsV2` paginado + `DeleteObjects` em lotes de até 1000, seguido de remoção dos metadados do usuário. Exposto só como método do `IImageService` (sem endpoint), para o futuro fluxo de exclusão de conta.

**Rationale**: FR-017. `DeleteObjects` exige checksum no corpo; com `WHEN_REQUIRED` o SDK continua calculando-o (é obrigatório pela API S3).

## §11 Endpoints e códigos HTTP

**Decision** (detalhes em [contracts/images-api.md](./contracts/images-api.md)):

| Operação | Rota | Sucesso | Erros |
|---|---|---|---|
| Declarar envio | `POST /images/uploads` | 201 (novo ou pendente reaproveitado com URL nova) / 200 (já disponível) | 400, 401, 503 |
| Confirmar envio | `POST /images/uploads/{imageId}/confirm` | 200 | 400, 401, 404, 503 |
| Gravar pelo servidor | `POST /images` (multipart, campo `file`) | 201 / 200 (já disponível) | 400, 401, 413, 503 |
| Consultar | `GET /images/{imageId}` | 200 (`url` só se `available`) | 401, 404, 503 |
| Excluir | `DELETE /images/{imageId}` | 204 | 401, 404, 503 |

Storage fora do ar → `StorageUnavailableException` (Domain) → **503** `ErrorResult` "Armazenamento de imagens indisponível." sem detalhe. A Infra captura `AmazonServiceException`/`HttpRequestException` e relança a exceção de domínio com a original apenas como `InnerException` (nunca no `Message`), para que o `catch (Exception) → 500 ex.Message` da constituição não exponha endereço, bucket ou chave.

**Upload pelo servidor**: `[RequestSizeLimit(MaxUploadBytes + 64 KB)]` no endpoint; o gateway já aceita até 20 MB (`client_max_body_size 20m`), acima do limite padrão de 10 MB. Excedido → 413 do Kestrel / 400 da validação.

## §12 Configuração e validação no startup

**Decision**: `ObjectStorageSettings` em `Ambrosio.DTO/Settings`, ligado à seção `ObjectStorage`, com validação em `ValidateSettings` (`Program.cs`), no mesmo estilo de `CpfProtection`:

| Chave | Padrão | Regra |
|---|---|---|
| `ServiceUrl` | — | obrigatória, URI absoluta http/https |
| `PublicServiceUrl` | = `ServiceUrl` | URI absoluta se informada |
| `Region` | `us-east-1` | obrigatória |
| `Bucket` | — | obrigatória |
| `ForcePathStyle` | `false` | — |
| `AccessKey` / `SecretKey` | — | obrigatórias (segredos) |
| `PresignedReadMinutes` | 15 | 1–60 |
| `PresignedUploadMinutes` | 5 | 1–15 |
| `MaxUploadBytes` | 10485760 | > 0 |
| `AllowedMimeTypes` | jpeg, png, webp | não vazia; só tipos que o processador reconhece |
| `PendingTtlMinutes` | 60 | 1–1440 |
| `CleanupIntervalMinutes` | 15 | 1–60 |

A mensagem de erro nomeia a variável (`ObjectStorage__Bucket está vazio.`) e nunca o valor (FR-020, FR-021).

## §13 Ambientes

**Decision**:

| Ambiente | Storage | `ServiceUrl` / `PublicServiceUrl` | Bucket | Prazos (leitura/envio/pendente/intervalo, min) |
|---|---|---|---|---|
| Development (`dotnet run`) | MinIO da homologação, porta exposta no host | `http://localhost:9000` / igual | `ambrosio-dev` | 15 / 5 / 60 / 15 |
| Homolog (compose) | serviço `storage` (MinIO) + `storage-init` | `http://storage:9000` / `http://localhost:${STORAGE_PORT:-9000}` | `ambrosio-homolog` | **1 / 1 / 2 / 1** (curtos, para os testes de expiração e limpeza) |
| Production | DigitalOcean Spaces | `https://<região>.digitaloceanspaces.com` / igual | `ambrosia-prod` | 15 / 5 / 60 / 15 |

- Valores não secretos em `appsettings.{Ambiente}.json`; `AccessKey`/`SecretKey` vêm do `.env` (homolog) e dos GitHub Secrets (`OBJECT_STORAGE_ACCESS_KEY`, `OBJECT_STORAGE_SECRET_KEY`) em produção. Development guarda as credenciais locais do MinIO no `appsettings.Development.json` (valores de dev, como o JWT de dev).
- Os prazos curtos de homolog são aplicados por variáveis no compose (`ObjectStorage__PresignedReadMinutes` etc.), com padrão sobrescrevível pelo `.env`.

**Imagem do MinIO**: tag `RELEASE.*` fixa (nunca `latest`), tanto para `minio/minio` quanto para `minio/mc`. A distribuição de binários/imagens da edição comunitária do MinIO mudou em 2025; verificar na implementação que a tag escolhida continua disponível. Se não houver tag utilizável, o substituto é outro servidor S3 compatível auto-hospedado (ex.: SeaweedFS ou Garage), sem mudança no código da API — só no compose.

**Criação dos buckets**: serviço `storage-init` (imagem `minio/mc`, `restart: "no"`) que roda após o `storage` ficar saudável: `mc alias set`, `mc mb --ignore-existing` para `ambrosio-homolog` e `ambrosio-dev`, `mc anonymous set none` em ambos. A `api` depende de `storage-init` com `service_completed_successfully` (SC-009).

## §14 CORS do bucket

**Decision**:
- **Homolog/Dev (MinIO)**: CORS por variável de servidor `MINIO_API_CORS_ALLOW_ORIGIN=http://localhost:8080,http://localhost:5173` (MinIO não aplica CORS por bucket). Os dois buckets locais aceitam as duas origens.
- **Production (Spaces)**: regra CORS no bucket `ambrosia-prod` com `AllowedOrigins = https://ambrosiaai.site`, `AllowedMethods = GET, PUT, HEAD`, `AllowedHeaders = Content-Type`, `MaxAgeSeconds = 3600`, aplicada uma vez (painel ou `PutBucketCors`) e documentada em `deploy/README.md`.

**Desvio registrado**: FR-022 pede CORS "por ambiente" em cada bucket local. No MinIO a regra é do servidor, então `ambrosio-dev` e `ambrosio-homolog` aceitam as duas origens locais. Só afeta ambientes locais com dados descartáveis; em produção a regra é exatamente a origem única. Registrar em `decisions.md`.

## §15 CSP do frontend

**Decision**: o `frontend/nginx.conf` vira **template** (`/etc/nginx/templates/default.conf.template`, processado pelo `envsubst` da imagem oficial do nginx no startup) com a variável `STORAGE_ORIGIN`, acrescentada a `img-src` e `connect-src` nas quatro ocorrências da CSP. O compose define `STORAGE_ORIGIN=http://localhost:9000` (homolog) e `https://ambrosia-prod.<região>.digitaloceanspaces.com` (produção). O `img-src ... https:` existente permanece porque o avatar do Google vem de domínios `googleusercontent.com`.

**Rationale**: FR-023 sem rebuild por ambiente. Nenhum código React muda nesta feature.

**Alternatives considered**: CSP por `BUILD_MODE` no build — exigiria rebuild para trocar o endpoint do storage.

## §16 Testes de API

**Decision**: nova classe `ImagesControllerTests` (skill `dotnet-test-api`), com fixtures binárias em `Ambrosio.ApiTests/Fixtures/Images/` (JPEG com EXIF GPS, PNG, WebP, PDF renomeado `.jpg`). O SHA-256 é calculado no próprio teste. O `PUT` pré-assinado e o `GET` sem assinatura são feitos com `HttpClient` simples, direto no storage.

- O endereço do objeto sem assinatura é a `url` de leitura sem a query string.
- Testes de expiração (leitura, envio) e de limpeza leem os prazos esperados de `appsettings.Test.json` (`Storage:PresignedReadMinutes`, `Storage:PresignedUploadMinutes`, `Storage:PendingTtlMinutes`, `Storage:CleanupIntervalMinutes`) e são **pulados** quando a espera ultrapassaria 5 minutos (ex.: contra o Spaces com prazos de produção). Contra homolog rodam sempre (~1–4 min cada).
- A verificação de que o objeto sumiu após exclusão/recusa usa o `GET` assinado obtido antes: com o objeto apagado o storage responde 404 (`NoSuchKey`), equivalente ao `HeadObject` do critério 10 da HU, sem dar credenciais do storage aos testes.
- Nenhum teste unitário do backend é criado (constituição §VIII).

## §17 Observabilidade e segredos

**Decision**: logs estruturados apenas com `imageId`, `userId`, status e motivo da recusa. Nunca registrar `uploadUrl`, URL de leitura, `AccessKey`/`SecretKey` nem `ServiceUrl` com credenciais. O logging interno do AWS SDK permanece desligado (padrão). O Swagger documenta os DTOs, que não têm campos de segredo.
