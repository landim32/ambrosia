# Decisions log — 004-s3-image-storage

Registro das verificações pedidas em `tasks.md` e dos desvios aceitos. Cada seção corresponde à
âncora citada na tarefa.

---

## tasks · baseline

Verificado em 2026-10-26 (T001), antes de qualquer alteração de código.

| Checagem | Comando | Resultado |
|---|---|---|
| Build da solução | `dotnet build backend/Ambrosia.sln` | **Compilação com êxito — 0 Aviso(s), 0 Erro(s)** (5,13 s) |
| Suíte de API contra homolog | `ApiBaseUrl=http://localhost:8080/api dotnet test backend/Ambrosia.ApiTests` | **Aprovado! — Com falha: 0, Aprovado: 99, Ignorado: 0, Total: 99** |

**Falhas pré-existentes: nenhuma.** A linha de base está verde, então qualquer falha depois das
tarefas desta feature é attributable a ela.

Duas condições do ambiente notadas durante a medição e resolvidas com autorização do usuário:

- Havia uma API de Development no ar (`Ambrosia.API.exe --url http://localhost:5000`, PID 22348)
  travando os DLLs de `bin/` e impedindo o build do `Ambrosia.API`. O processo foi encerrado
  (`backend/scripts/stop-api.ps1` é o caminho normal de parar/relançar).
- A homologação estava **parcial**: `db`, `api`, `frontend` e `gateway` sujos/rodando, sem
  `migrator`/`storage-init` (não existem ainda). Os checkpoints de reset do zero (T058/T069) ficam
  **sem reset de volume** por decisão do usuário — ver "tasks · ambientes".

## tasks · imagem do storage

**A imagem MinIO não está mais disponível. Homolog e Development passam a usar o DigitalOcean
Spaces real (decisão do usuário), e os serviços `storage`/`storage-init` do compose não existem.**

Verificações (T002, o "verificar na implementação" do research §13):

| Checagem | Resultado |
|---|---|
| `docker pull minio/minio:latest` | `pull access denied for minio/minio, repository does not exist` |
| Docker Hub `GET /v2/repositories/minio/minio/` e `/tags` | `{"message":"object not found"}` |
| Docker Hub `GET /v2/repositories/minio/` (20 repos, página 1) | sem `minio` e sem `mc` na relação |
| Docker Hub `GET /v2/repositories/minio/mc/tags` | `{"message":"object not found"}` |
| `docker pull quay.io/minio/minio:latest` e `quay.io/minio/mc:latest` | `unauthorized: access to the requested resource is not authorized` |
| Docker Hub `bitnami/minio/tags` | `{"count":0}` (catálogo sem tags) |

O research §13 previa exatamente isto ("a distribuição de binários/imagens da edição comunitária do
MinIO mudou em 2025") e apontava SeaweedFS ou Garage como substitutos auto-hospedados. **O usuário
recusou a troca por outro servidor local**: o storage de homolog e Development é o **S3 real da
DigitalOcean**, não um servidor em container.

Consequências no plano (registradas aqui e aplicadas nas tarefas afetadas):

| Tarefa | Antes (plano) | Agora |
|---|---|---|
| T002 | fixar tags `RELEASE.*` de `minio/minio`/`minio/mc` | sem imagem: nada a fixar |
| T008 | serviços `storage` + `storage-init` no compose | **não existe**: o storage é externo ao compose |
| T009 | `ObjectStorage__*` apontando para `http://storage:9000` | `ObjectStorage__*` apontando para `https://nyc3.digitaloceanspaces.com` |
| T010 | `STORAGE_ROOT_USER/PASSWORD` + portas 9000/9001 | `OBJECT_STORAGE_ACCESS_KEY`/`OBJECT_STORAGE_SECRET_KEY` + `OBJECT_STORAGE_BUCKET`/`OBJECT_STORAGE_PUBLIC_ORIGIN` |
| T011 | `mc ls` + `curl` no bucket local | criação/provisionamento do bucket no Spaces (CORS + bloqueio público) por script |
| T059 | "rodar a suíte contra um Spaces de teste" | **sem efeito**: a suíte inteira já roda contra Spaces — SC-001 é o caminho padrão |
| T058 | "homolog do zero" com `down -v` | reduzido: sem reset de volume (decisão do usuário); o que se prova é "sobe sem nenhum passo manual de storage", porque o storage nem é local |

Ganhos colaterais desta troca:

- O desvio registrado no plan.md ("CORS por servidor no MinIO, os dois buckets locais aceitam as
  duas origens") **desaparece**: o Spaces aplica regra CORS **por bucket**, exatamente como pede a
  FR-022. Ver "tasks · CORS" ao provisionar.
- `PublicServiceUrl` deixa de ser necessário na prática (o navegador alcança o mesmo endpoint HTTPS
  público que a API alcança). A propriedade continua existindo por ser a solução do research §3 para
  qualquer servidor local futuro; nos três ambientes ela fica vazia (= `ServiceUrl`).
- Um único bucket (`ambrosio`, região `nyc3`) atende Development e homologação, separados por
  `users/{userId}/images/...` no contexto de testes de API. Production usa bucket próprio (T056).

Riscos novos, a confirmar nos testes:
- O Spaces tem de aceitar as pré-assinaturas SigV4 do SDK 4.x com `ForcePathStyle = false`
  (virtual-host style) e o `Content-Type` assinado no `PUT` (cenários 1–3 da US1).
- O `DeleteObjects` em lote (usado só por `DeletePrefixAsync`, sem endpoint e sem teste de API) exige
  checksum no corpo; com `WHEN_REQUIRED` o SDK calcula. Se o Spaces recusar, o problema aparece só no
  fluxo interno de exclusão de conta (FR-017) e é registrado aqui.

## tasks · licença ImageSharp

Verificação de elegibilidade da *Six Labors Split License* v1.0 para o Ambrosia (research §7):

- O repositório é **open source sob a MIT License** (`LICENSE`, "Copyright (c) 2026 Rodrigo Landim
  Carneiro"), com `git remote` público em `github.com/landim32/ambrosia`. A Split License libera o
  uso sem custo para projetos licenciados sob licença aprovada pela OSI (MIT está entre elas), desde
  que o aviso de licença/autoria seja mantido — condição cumprida pelo próprio `LICENSE` no repositório.
- Não há dependência de assinatura comercial para esta feature. **Como a Split License é verificada
  a cada release do ImageSharp, a checagem deve ser repetida antes do deploy em produção** (item já
  listado em `deploy/README.md`, T056).
- Versão escolhida: **SixLabors.ImageSharp 3.2.0** — **não** a última 3.1.x que o research §7 pediu
  para confirmar. O `dotnet restore` com 3.1.12 emitiu **seis avisos de vulnerabilidade conhecida**
  (NU1902/NU1903), e esta biblioteca decodifica o binário que o usuário envia:

  | GHSA | CVE | Sev. | Vulnerável | Corrigido em |
  |---|---|---|---|---|
  | GHSA-j3p4-wp97-rph4 | CVE-2026-106113 | high | `>=2.0.0,<3.2.0` | 3.2.0 / 4.1.2 |
  | GHSA-j9gm-c75j-xc9q | CVE-2026-106110 | high | `>=2.0.0,<3.2.0` | 3.2.0 / 4.1.2 |
  | GHSA-jjfr-hcj7-qf5w | CVE-2026-106115 | high | `>=2.1.0,<3.2.0` | 3.2.0 / 4.1.2 |
  | GHSA-v76p-62qx-wwq2 | CVE-2026-106118 | high | `>=3.0.0,<3.2.0` | 3.2.0 / 4.1.1 |
  | GHSA-gwg2-r3hj-4w44 | CVE-2026-106114 | medium | `>=1.0.0-beta0001,<3.2.0` | 3.2.0 / 4.1.2 |
  | GHSA-wmxv-xphr-5c9g | CVE-2026-106116 | medium | `>=2.0.0,<3.2.0` | 3.2.0 / 4.1.2 |

  As seis se fecham em **3.2.0**, o primeiro release da série 3.2 (NuGet: `3.1.12 → 3.2.0 → 4.0.0 →
  4.1.3`). Ficamos no mesmo *major* 3.x que o research escolheu — os APIs usados
  (`DetectFormatAsync`, `IdentifyAsync`, `AutoOrient`, perfis EXIF/IPTC/XMP, encoders JPEG/WebP) não
  mudam de contrato — sem carregar um decodificador com escrita fora do heap comprovada. Subir para
  4.1.3 ficaria igualmente verde, mas é quebra de *major* fora do escopo desta feature.
- **O gate de licença é do build, e só trava em `Release`.** O pacote 3.2.0 trouxe
  `build/SixLabors.ImageSharp.targets` + `SixLabors.Licensing.dll` (o 3.1.12 tem apenas o `.props`), e o
  alvo passa `IsDebugBuild` e `ContinueOnError` ao `ValidateLicenseTask`. Medido nesta sessão:
  `dotnet build` em `Debug` conclui **com êxito**, carregando 4 avisos
  (`No Six Labors license found. This Debug build can continue, but Release builds require a valid
  license.`); em `Release` o mesmo build **para** com `error : No Six Labors license found`. Como o
  `backend/Dockerfile` publica em `Release` (`dotnet publish -c Release`, e o bundle de migrações com
  `--configuration Release`), **nenhuma imagem de homologação ou produção sai sem a licença** — nem com
  3.2.0, nem com 4.1.3.
  A linha de base tinha 0 avisos, então os 4 avisos de licença são o preço momentâneo de rodar em `Debug`
  sem a chave; somem quando o `backend/sixlabors.lic` (ou `SixLaborsLicenseKey`) estiver no lugar, e o
  `backend/Directory.Build.props` criado nesta feature já o apanha da raiz do `backend/`. Para o
  `docker build`, o arquivo precisa estar no contexto (o `COPY . .` do `backend/Dockerfile` já o traz).
- A **alternativa sem licença** seria ficar na 3.1.12 (ninguém nega o build, mas o decodificador fica
  com as seis falhas acima). A 4.1.3 tem o mesmo gate da 3.2.0, então não é saída para a licença — é só
  um *major* mais recente.
- Não há API de licença em tempo de execução: as XML docs do pacote 3.2.0 não mencionam licença em
  nenhum tipo público, ou seja, o binário funciona com a validação feita no build.

## tasks · versões dos pacotes (T004)

| Pacote | Camada | Versão | Justificativa |
|---|---|---|---|
| `AWSSDK.S3` | Infra | `4.0.104.7` | última 4.x estável do NuGet no dia (research §1) |
| `SixLabors.ImageSharp` | Infra | `3.2.0` | última que fecha as seis advisories conhecidas (seção acima) |
| `Microsoft.Extensions.Hosting.Abstractions` | Application | `9.0.22` | exigido pelo `BackgroundService`; alinha o grafo `Microsoft.Extensions.*` 9.0.22 que a solução já usa (evita NU1605) |

Duas adições de pacote que o `tasks.md` não previu, ambas forçadas pelo §3 da constituição (Domain não
conhece implementações de infra, mas a *abstração* é permitida — é o mesmo caso do FluentValidation):

| Pacote | Camada | Por quê |
|---|---|---|
| `Microsoft.Extensions.Options` 9.0.22 | Domain | T032 pede `IOptions<ObjectStorageSettings>` no validator, e o T024 pede no `ImageService`. Sem isto o Domain não lê limite nem tipo aceito nenhum |
| `Microsoft.Extensions.Logging.Abstractions` 9.0.22 | Domain | T024 pede `ILogger<ImageService>` no construtor. É só a interface — nenhuma implementação de logging entra no Domain |

## validação de startup (T051, T052)

Medido em 2026-10-27 com o host em `Debug` (`dotnet exec bin/Debug/net8.0/Ambrosia.API.dll`), config
passada por argumento de linha de comando — que tem precedência sobre as variáveis de ambiente e, ao
diferença delas, aceita valor vazio de verdade (`set VAR=` no Windows apaga a variável em vez de
esvaziá-la, o que escondia os casos de credencial vazia).

| Caso | Esperado | Resultado |
|---|---|---|
| configuração válida (control) | host sobe | **sobe** |
| `ObjectStorage:Bucket` vazio | recusa + `ObjectStorage__Bucket está vazio.` | ✓ |
| `ObjectStorage:AccessKey` vazio | recusa + nomeia a variável | ✓ |
| `ObjectStorage:SecretKey` vazio | recusa + nomeia a variável | ✓ |
| `ObjectStorage:ServiceUrl` relativo | recusa + exige URL absoluta http/https | ✓ |
| `ObjectStorage:Region` vazio | recusa | ✓ |
| `PresignedReadMinutes` = 0 e = 61 | recusa (faixa 1-60) | ✓ nos dois |
| `PresignedUploadMinutes` = 16 | recusa (faixa 1-15) | ✓ |
| `PendingTtlMinutes` = 2000 | recusa (faixa 1-1440) | ✓ |
| `CleanupIntervalMinutes` = 0 | recusa (faixa 1-60) | ✓ |
| `MaxUploadBytes` = 0 | recusa (deve ser > 0) | ✓ |
| `AllowedMimeTypes:0` = `image/tiff` | recusa (só jpeg/png/webp) | ✓ |

**12 de 12 casos inválidos recusaram o startup; o caso válido subiu.** Vazamento de segredo: foi colocada
uma credencial-canario recognizável (`CANARY-SEGREDO-…`) em `AccessKey` e `SecretKey` de **todos** os
casos, e a string não aparece em nenhum stdout/stderr capturado — a mensagem cita só o **nome** da
variável (FR-020, FR-021). Script: `.tmp-tools/t052c.ps1` (fora do controle de versão; o passo é
reproduzível pelo quickstart §9).

## revisão de segurança (T067)

Feita em 2026-10-27 por inspeção do código novo, item por item do checklist da tarefa.

**1. Nenhuma rota nova sem autenticação.** `ImagesController` declara `[Authorize]` na classe e as cinco
ações (`POST /images/uploads`, `POST /images/uploads/{id}/confirm`, `POST /images`,
`GET /images/{id}`, `DELETE /images/{id}`) estão dentro dela; não há `[AllowAnonymous]` em lugar nenhum do
arquivo. A varredura que prova isso por HTTP está em T040 (as cinco operações entram em
`UnauthorizedAccessTests.ProtectedOperations`) e **ainda não rodou contra o código novo**.

**2. IDOR / isolamento por usuário.** Toda leitura e toda escrita passam o `userId` do claim `sub` e a
cláusula do dono é a **primeira** no `Where`:

| Caminho | Filtro verificado |
|---|---|
| `GetByIdAsync` | `.Where(i => i.UserId == userId)` antes de `FirstOrDefaultAsync(i => i.Id == imageId)` |
| `GetActiveByHashAsync` | dono primeiro, depois hash e status |
| `UpdateAsync` | `i.Id == entity.Id && i.UserId == entity.UserId` |
| `DeleteAsync` | `i.Id == entity.Id && i.UserId == entity.UserId` no próprio `DELETE` |
| `DeleteAllByUserAsync` | por `userId` |

`StorageKey` nunca sai de um DTO nem entra em um: é sempre derivado no servidor por
`StoredImage.BuildStorageKey(userId, ...)`, então não existe parâmetro de requisição que escolha a chave
do objeto. O `userId` vem de `User.GetUserId()` em todas as ações; nenhum DTO de entrada tem campo de
dono.

**3. Nada de URL assinada, chave de objeto ou credencial em log.** Os 12 pontos de log do código novo
foram lidos um a um: `ImageService` registra apenas `imageId`, `userId`, `sizeBytes`, dimensões, contagem
da limpeza e o motivo da recusa; `PendingImageCleanupService` registra intervalos/TTL e a exceção do
ciclo; `ObjectStorageAppService` registra o nome da operação (`HeadObject`, `PutObject`...) e a exceção
original como `InnerException` do log — nunca como mensagem de resposta. Não há um único log que
interpole `PresignedUrl.Url`, `AccessKey`, `SecretKey` ou `StorageKey`. A busca por `X-Amz-Signature` e
pelas chaves nos logs do container é a parte que precisa do ambiente rodando (em aberto, ver
"checkpoints de teste").

**Correção aplicada durante esta revisão** (era o único vazamento real que encontrei): o `catch (Exception
ex)` genérico dos controllers devolve `ex.Message` no corpo do 500. `IsStorageFailure` embrulhava
`AmazonServiceException`, `HttpRequestException` e `TaskCanceledException`, mas **não**
`AmazonClientException` — que é o que o SDK lança quando falha ao *montar* ou *resolver* a requisição e
traz o endpoint, o bucket e a chave do objeto no texto. Uma falha dessas viraria 500 com a estrutura do
storage revelada (FR-021). `ObjectStorageAppService.IsStorageFailure` passou a incluir
`AmazonClientException`, e essa mensagem agora só existe como `InnerException`.

**4. Limites sobre entrada não confiável.** `MaxUploadBytes` é imposto três vezes: no validator da
declaração, no `ContentLength` do `HeadObject` antes de baixar, e na cópia limitada (`CopyBoundedAsync`,
que aborta ao passar do teto). O multipart tem `[RequestSizeLimit]` no controller, e o gateway tem
`client_max_body_size 20m`. O tipo é decidido pelo conteúdo (`DetectFormatAsync`), nunca pelo nome ou
pelo cabeçalho, e o startup recusa `AllowedMimeTypes` fora de jpeg/png/webp (research §12), então não dá
para cadastrar um tipo que o processador não sabe tratar.

**5. EXIF/GPS no caminho direto — o desvio que precisa estar visível.** O `PUT` pré-assinado grava os
bytes **como o cliente os enviou** (clarificação 2 da spec, aceita no plan.md): se a vendedora arrastar
uma foto de celular com GPS, o metadado fica no objeto. O que protege a localização nesse caminho é a
privacidade do bucket + assinatura curta + 404 depois de excluir, e a sanitização só existe no caminho do
servidor (`POST /images`, que remove EXIF/IPTC/XMP e re-codifica). O critério 11 da HU 003 lê
"gravada**/sanitizada**", então a leitura restritiva está registrada como desvio aceito aqui e no
Complexity Tracking — não passou despercebida.

**6. CORS.** Com Spaces a regra é **por bucket** (o desvio "por servidor" do plano desapareceu). Como
Development e homologação compartilham o bucket `ambrosio`, as origens `localhost:8080`/`localhost:5173`
vivem no mesmo bucket dos dois, e o bucket de produção terá só `https://ambrosiaai.site` — aceito porque
o bucket de produção é separado e os dados locais são de teste.

**7. A exceção ao §VI.** `ListStaleAsync` é a única leitura de repositório sem filtro de usuário, e não
tem caminho de endpoint: quem a chama é `PendingImageCleanupService` via `IImageService.PurgeStaleAsync`,
e nenhum método de `ImagesController` a alcança. Há teste que cobre a ausência de rota de massa
(`DeleteAllForUser_ShouldNotBeReachableByAnyRoute` varre os caminhos do documento OpenAPI), rodando quando
o ambiente estiver de pé.

## remoção do ImageSharp (2026-10-27)

Decisão do usuário: o `SixLabors.ImageSharp` saiu do projeto **sem substituto**. O 3.2.0 exige licença
da Six Labors no build `Release`, o que travava o `docker compose build`. Consequências:

- **Sem detecção de tipo pelo conteúdo** (FR-005/FR-006 na parte "tipo real"): no envio direto, o tipo
  fica garantido pela declaração (só tipos aceitos) e pelo `Content-Type` assinado na URL de `PUT`; o
  storage recusa outro tipo. Um arquivo que não é imagem, mas enviado como `image/jpeg`, **passa**. Na
  gravação pelo servidor vale o `Content-Type` do multipart, conferido contra `AllowedMimeTypes`.
- **Sem dimensões**: `width`/`height` ficam sempre `null` (o contrato já os permitia nulos).
- **Sem sanitização** (FR-010 retirado): EXIF/GPS, IPTC e XMP permanecem nos arquivos gravados pelo
  servidor, e não há re-codificação contra arquivos "poliglotas". O binário é gravado como chegou.
- Removidos: `IImageProcessingAppService`, `ImageProcessingAppService`, `ImageInspection`,
  `ProcessedImage`, `backend/Directory.Build.props` e o fixture `fake.jpg`; os testes de API
  `Confirm_FakeJpeg_ShouldReturn400ByType` e o de remoção de EXIF deixaram de existir.
- `Program.ValidateSettings` deixou de rodar no design time do EF: o `dotnet ef migrations bundle` do
  Dockerfile monta o host sem as chaves do storage e era recusado (o erro ficava escondido atrás do
  da licença).

As seções "tasks · licença ImageSharp" e os desvios 4, 5 e 9 abaixo ficam como histórico.

## desvios aceitos e mudanças de escopo (T068)

Além dos dois já registrados no Complexity Tracking do plan.md (pré-assinatura a cada resposta; EXIF
preservado no envio direto), esta implementação acumulou:

| # | Desvio | Motivo | Risco |
|---|---|---|---|
| 1 | Storage local eliminado: homolog e Development usam Spaces real, sem container | `minio/minio`/`minio/mc` saíram do Docker Hub | custo/rede em testes; sem storage offline |
| 2 | Bucket único (`ambrosio`) para Development **e** homologação | decisão do usuário | ambientes locais compartilham objetos; separação é só por `users/{id}/` |
| 3 | Credencial de Development vem de variável de ambiente, não do `appsettings.Development.json` | é credencial de conta real, não valor de bancada | API não sobe sem exportar as duas chaves |
| 4 | ImageSharp 3.1.x → **3.2.0** | as seis advisories da 3.1 só se fecham na 3.2 | build `Release` exige licença |
| 5 | Licença Six Labors necessária no build `Release` | o 3.2.0 traz o alvo de MSBuild que valida licença | **bloqueia `docker compose build` e o deploy** até registrar `backend/sixlabors.lic` |
| 6 | Domain ganhou `Microsoft.Extensions.Options` + `Logging.Abstractions` | T024/T032 pedem `IOptions`/`ILogger` no Domain | duas dependências novas na camada mais interna (só abstrações) |
| 7 | T026 e T035 executados juntos | um controller sem ações deixaria campo privado não usado (aviso CS0414) e o plano proíbe warns | nenhum |
| 8 | Testes escritos depois da implementação nas histórias | a fase "vermelho" não era observável: sem credencial o ambiente não sobe, e o container em produção roda imagem anterior à 004 | perda do check de que o teste falhava pelo motivo certo |
| 9 | Rodada de verificação em `Debug` local em vez de homolog Docker | licença ausente no build `Release` (decisão do usuário) | desvio da constituição §VII; volta para homolog quando a licença sair |
| 10 | `AmazonClientException` incluído no invólucro de indisponibilidade | vazaria endpoint/bucket/chave no corpo do 500 | um erro de programação no SDK passa a aparecer como 503 em vez de 500 |
| 11 | `ObjectStorage__Bucket` e `OBJECT_STORAGE_PUBLIC_ORIGIN` sobrescrevíveis pelo compose | o nome do bucket no Spaces é global e pode estar tomado | nenhum |

Renomeações do SDK 4.x descobertas na implementação (útil quando alguém comparar com a 3.x):
`PutCORSAsync`/`PutCORSRequest` → `PutCORSConfigurationAsync`/`PutCORSConfigurationRequest`, e a
propriedade do pedido é `Configuration` (não `CORSConfiguration`); `ListObjectsV2Response.IsTruncated` é
`bool?`; `PutObjectRequest` não tem `Content` (é `ContentBody`); o encoder WebP é
`SixLabors.ImageSharp.Formats.Webp.WebpEncoder`. O EF Core 9 usa `HasSentinel`, não `HasSentinelValue`.

## tasks · SC-001, SC-005, SC-009, SC-011, SC-012

**Em aberto — sem medição nesta rodada.** Os cinco dependem de rodar a suíte contra o código novo, o que
está bloqueado pelas credenciais do Spaces e pela licença do ImageSharp (ver "checkpoints de teste"). O
que já se pode afirmar, e o que falta para cada um:

- **SC-001** (≤0,5% rejeitados por incompatibilidade): a suíte inteira roda contra Spaces, que *é* o
  teste; o número só sai da contagem de testes de storage verdes. Era o papel do T059, que assim passa de
  "rodada extra" a caminho padrão.
- **SC-005** (reenvio do mesmo conteúdo em <2 s): `RequestUpload_SameAvailableContent_ShouldReturn200AlreadyExists`
  mede o comportamento; falta o tempo, que deve ser anotado do output do `dotnet test`.
- **SC-009** (ambientes do zero só com configuração): **parcial por decisão** — você escolheu não fazer o
  reset de volume, e o `docker compose up -d --build` está bloqueado pela licença. O que a arquitetura já
  prova: não há nenhum passo manual de storage por ambiente (o bucket é externo e pré-existente), e
  nenhum arquivo de dados/versionado de imagem entrou no repositório.
- **SC-011** (nenhuma rota de tela alterada): cumprido por construção — esta feature não tocou em
  nenhuma rota do frontend; o único arquivo de frontend alterado é o config do container
  (`frontend/nginx/default.conf.template`, `frontend/Dockerfile`) e o `frontend/src/**` ficou intacto.
- **SC-012** (fluxo de reordenar sem erro de contrato): depende do seletor de imagem da HU 005, que não
  existe ainda; o contrato que ele vai consumir (`images-api.md`/`openapi.yaml`) é o que está publicado.

Quando a rodada acontecer, reescrever esta seção com os números reais: total de testes, falhas, tempo do
reenvio duplicado e a contagem de rejeições por incompatibilidade de storage.

## checkpoints de teste — status da rodada

A suíte de API (`Ambrosia.ApiTests`) foi escrita inteira (US1 a US6 + varredura de 401) e **compila**,
mas ainda **não rodou contra o código novo**. Dois bloqueios, ambos do lado de fora do código-fonte:

1. **Credenciais do Spaces.** O `.env` local ainda não tem `OBJECT_STORAGE_ACCESS_KEY`/
   `OBJECT_STORAGE_SECRET_KEY` (conferido em 2026-10-27), e sem elas a API recusa subir por projeto — o
   que é a FR-020 funcionando, não um defeito. Sem o bucket `ambrosio` provisionado com a regra CORS, os
   testes de `PUT`/`GET`/preflight também não têm com o que falar. Por isso **T010, T011, T036, T058,
   T059 e T069 continuam abertos**. O provisionador está pronto (cria o bucket se faltar, aplica
   `PublicAccessBlock`, grava a regra CORS para as duas origens locais, faz `PUT`+`GET` anônimo de prova e
   um `OPTIONS` de preflight); rode quando as chaves estiverem no `.env`.
2. **Licença do ImageSharp no `Release`.** O `docker compose build` da homologação publica em `Release`
   e para com `No Six Labors license found`. A decisão do usuário para esta rodada foi **rodar tudo local
   em `Debug`** (que só avisa), então o checklist de homologação do quickstart §3.1 fica para quando a
   licença for registrada — ver "tasks · licença ImageSharp" e a seção de licença em `deploy/README.md`.

A varredura de 401 contra o container que está no ar hoje **não vale medida**: o
`ambrosia-homolog-api` em execução é a imagem **anterior** à 004 — `GET /api/images/1` responde o 404 de
página do nginx, não o 404 JSON da API, e as rotas novas simplesmente não existem lá. Rodando a suíte
contra ele, os 101 testes anteriores continuam verdes e os 49 novos falham por rota inexistente, que é o
resultado esperado e não uma falha desta implementação.

Contra a Development local, a suíte nova falha inteira **antes de qualquer asserção**, pelo guard do
fixture (medido em 2026-10-27): 31 de 31 testes de `ImagesControllerTests` em 158 ms com

```text
System.InvalidOperationException : A API Ambrosia não está acessível em http://localhost:5000
(FlurlHttpException: ... a máquina de destino as recusou ativamente). Suba-a em Development antes de
rodar Ambrosia.ApiTests: backend/scripts/run-api.ps1.
```

Ou seja: nenhuma das falhas é atribuível ao código da feature — é o passo 1 do quickstart que não foi
dado, e ele depende das credenciais. Os 101 testes anteriores permanecem verdes contra o container antigo,
e a compilação da solução está limpa (0 erros).
