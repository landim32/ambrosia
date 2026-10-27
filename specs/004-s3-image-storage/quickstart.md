# Quickstart — Validação: Storage de imagens compatível com S3 (privado, URLs pré-assinadas, sanitização)

**Correspondência de histórias**: US1 → §4, US2 → §5, US3 → §6, US4 → §7, US5 → §8 e §9, US6 → §10.

**Feature**: `004-s3-image-storage` | **Base técnica**: [plan.md](./plan.md) | **Contrato**: [contracts/images-api.md](./contracts/images-api.md), [contracts/openapi.yaml](./contracts/openapi.yaml)

Este documento é o guia de verificação ponta a ponta da implementação. A validação de pré-implantação
(originalmente agendada para 2026-10-28) usa exatamente estes cenários contra um bucket de teste, e o
registro do resultado vai em `decisions.md` (seção "tasks · SC-001").

> **Mudança aplicada nesta revisão (T066)**: o plano previa um MinIO em container na homologação
> (`storage` + `storage-init`, buckets `ambrosio-homolog`/`ambrosio-dev`, porta 9000). As imagens
> `minio/minio` e `minio/mc` não existem mais no Docker Hub, e a decisão registrada em
> [`decisions.md`](./decisions.md) (seção "tasks · imagem do storage") é usar o **DigitalOcean Spaces
> real** em homolog e Development. Não há, portanto, serviço de storage no `docker-compose.yml`: o que
> existe é configuração (`ObjectStorage__*`) e um bucket provisionado fora do compose. As instruções
> abaixo refletem isso.

## 1. Pré-requisitos

- WSL2 com Docker Engine (`wsl -e docker info` responde).
- Postgres do banco de desenvolvimento acessível em `localhost:5432` (o dev roda a API fora do Docker):
  `powershell -File backend/scripts/ensure-postgres.ps1`.
- **Credenciais do Spaces no `.env`** (feature 004):

  ```bash
  OBJECT_STORAGE_ACCESS_KEY=<chave do Spaces>
  OBJECT_STORAGE_SECRET_KEY=<segredo do Spaces>
  OBJECT_STORAGE_BUCKET=ambrosio
  OBJECT_STORAGE_PUBLIC_ORIGIN=https://ambrosio.nyc3.digitaloceanspaces.com
  ```

  Sem `OBJECT_STORAGE_ACCESS_KEY`/`OBJECT_STORAGE_SECRET_KEY` a API **recusa subir** (FR-020) — ver §9.
- **Bucket `ambrosio` provisionado** (região `nyc3`), com bloqueio de acesso público ativo e a regra
  CORS do bucket autorizando `http://localhost:8080` e `http://localhost:5173` para `GET, PUT, HEAD`
  com o cabeçalho `Content-Type` (FR-022). É isso que faz o preflight do navegador responder
  `Access-Control-Allow-Origin`.
- Os prazos curtos de homologação/`dev-test` (1/1/2/1 min) já são o padrão do compose. Para simular os
  prazos de produção (15/5/60/15), sobrescreva `STORAGE_PRESIGNED_READ_MINUTES` etc. — os testes lentos
  de expiração e limpeza se pulam sozinhos quando o prazo configurado passa de
  `Storage:SlowTestMaxWaitMinutes`.
- `curl`, `jq` e `sha256sum` (Git Bash/WSL); `exiftool` só no §6.
- **Licença do ImageSharp** apenas para o build `Release` (o `docker compose --build` do backend e o
  deploy). Em `Debug` a falta é um aviso, então o caminho do §3.2 funciona sem licença.

## 2. Um arquivo de foto com EXIF/GPS

Os testes de sanitização precisam de um JPEG com metadado de GPS. Os fixtures da suíte de API servem, ou
gere um:

```bash
cp backend/Ambrosio.ApiTests/Fixtures/Images/small.jpg /tmp/foto-com-gps.jpg
exiftool -G1 -a -s /tmp/foto-com-gps.jpg | grep -i gps   # tem de mostrar GPSLatitude/GPSLongitude
```

## 3. Subir a API

### 3.1 Homologação em Docker (fiel à constituição §VII — exige a licença no build Release)

```bash
wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build
wsl --cd /mnt/c/repos/ambrosia -e docker compose ps
wsl --cd /mnt/c/repos/ambrosia -e docker compose logs --tail=60 migrator api
```

Esperado: `migrator` com *exited (0)* aplicando `AddStoredImages`, e `api`, `frontend`, `gateway`
*running*. Não se espera `storage` nem `storage-init`: o storage é externo ao compose. A partir daqui,
`API=http://localhost:8080/api`.

Se o build falhar com `No Six Labors license found`, use §3.2 ou registre a licença (ver
`deploy/README.md`).

### 3.2 Development em `Debug`, apontando para o mesmo Spaces (o caminho sem licença)

```powershell
cd backend
$env:ObjectStorage__AccessKey = '<chave>'      # ou importe do .env
$env:ObjectStorage__SecretKey = '<segredo>'
$env:ObjectStorage__PresignedReadMinutes = '1'   # prazos curtos como em homolog
$env:ObjectStorage__PresignedUploadMinutes = '1'
$env:ObjectStorage__PendingTtlMinutes = '2'
$env:ObjectStorage__CleanupIntervalMinutes = '1'
dotnet tool restore
dotnet ef database update --project Ambrosio.Infra --startup-project Ambrosio.API
powershell -File scripts/run-api.ps1
```

`appsettings.Development.json` já aponta para `https://nyc3.digitaloceanspaces.com` / bucket `ambrosio`
(`ForcePathStyle: false`). Aí `API=http://localhost:5000` (sem o prefixo `/api`, que é do gateway).
Esta é a via usada na rodada de 2026-10-26/27, porque a licença ainda não estava registrada; a
verificação final deve voltar para §3.1 (constituição §VII).

## 4. Fluxo de envio pelo cliente (US1)

```bash
API=http://localhost:5000          # ou http://localhost:8080/api
FILE=/tmp/foto-com-gps.jpg
SHA=$(sha256sum $FILE | cut -d' ' -f1); SIZE=$(stat -c%s $FILE)

TOKEN=$(curl -s -X POST $API/auth/register -H 'Content-Type: application/json' \
  -d '{"name":"Ana","email":"ana+'$RANDOM'@exemplo.com","password":"Senha-de-teste-123"}' | jq -r .token)

R=$(curl -s -X POST $API/images/uploads -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d "{\"mimeType\":\"image/jpeg\",\"sizeBytes\":$SIZE,\"sha256\":\"$SHA\"}")
ID=$(echo $R | jq -r .imageId); URL=$(echo $R | jq -r .uploadUrl)

curl -i -X PUT "$URL" -H 'Content-Type: image/png' --data-binary @$FILE    # 403 (tipo assinado != enviado)
curl -i -X PUT "$URL" -H 'Content-Type: image/jpeg' --data-binary @$FILE   # 200
curl -s -X POST $API/images/uploads/$ID/confirm -H "Authorization: Bearer $TOKEN" | jq \
  # status "available", width/height preenchidos, url pré-assinada
```

O `PUT` e o `GET` vão direto ao Spaces: o **Origin** da requisição é o host do bucket
(`ambrosio.nyc3.digitaloceanspaces.com`), não o da API, e o corpo nunca passa pelo gateway.

Confirmação repetida do mesmo id: **200** idempotente, com URL nova. Repetir o pedido de envio com o
mesmo `sha256`: **200** com `alreadyExists: true` e `image` preenchida (SC-005). Pedido com `mimeType`
`image/gif`, `sizeBytes` `0` ou `sha256` de 63 caracteres: **400** com `erros[].campo` apontando o campo.
Confirmar sem ter feito o `PUT`: **400** "O arquivo da imagem ainda não foi enviado." e o status continua
`pending` (critério 7 da HU).

## 5. Leitura, privacidade, isolamento e expiração (US2)

```bash
READ=$(curl -s $API/images/$ID -H "Authorization: Bearer $TOKEN" | jq -r .url)
curl -s -o /dev/null -w '%{http_code}\n' "$READ"          # 200
curl -s -o /dev/null -w '%{http_code}\n' "${READ%%\?*}"    # 403 (bucket privado, sem assinatura)
sleep 70; curl -s -o /dev/null -w '%{http_code}\n' "$READ" # 403 (expirou: prazo curto)
```

O binário baixado por `$READ` é byte a byte o que foi enviado (o `PUT` direto não é re-codificado) e a
URL muda a cada consulta. Com o token de uma segunda usuária (Bia): `GET /images/$ID` → **404**,
`POST /images/uploads/$ID/confirm` → **404**, `DELETE /images/$ID` → **404**, e nenhuma dessas respostas
contém a chave, o tipo ou o tamanho da imagem de Ana (SC-008).

## 6. Gravação pelo servidor e EXIF (US3)

```bash
curl -s -X POST $API/images -H "Authorization: Bearer $TOKEN" -F file=@$FILE | jq
# status "available", source "server", width/height presentes

curl -s "$(curl -s $API/images/<novo-id> -H "Authorization: Bearer $TOKEN" | jq -r .url)" -o /tmp/saida.jpg
exiftool -G1 -a -s /tmp/saida.jpg | grep -i gps    # nenhuma linha
exiftool -exeif:all /tmp/saida.jpg                  # vazio
```

No caminho do servidor espera-se um arquivo **diferente** do enviado (re-codificação) e sem EXIF/IPTC/XMP.
No caminho direto do cliente (§4) espera-se o oposto: bytes idênticos, porque não há sanitização nesse
fluxo (clarificação 2 da spec) — o que protege a localização é o bucket privado + URL assinada + 404 na
URL antiga depois de excluir. Um PDF renomeado `.jpg` (ou `.gif`): **400** com `erros[].campo = "file"`
ou a mensagem de tipo, nunca gravado.

## 7. Exclusão (US4)

```bash
READ=$(curl -s $API/images/$ID -H "Authorization: Bearer $TOKEN" | jq -r .url)
curl -i -X DELETE $API/images/$ID -H "Authorization: Bearer $TOKEN"   # 204
curl -s -o /dev/null -w '%{http_code}\n' "$READ"                       # 404 NoSuchKey (não é 403)
curl -s -o /dev/null -w '%{http_code}\n' $API/images/$ID -H "Authorization: Bearer $TOKEN"   # 404
```

## 8. CORS do bucket e storage indisponível (US5)

```bash
# preflight do upload direto, vindo da origem do frontend
curl -i -X OPTIONS "$URL" -H 'Origin: http://localhost:8080' -H 'Access-Control-Request-Method: PUT' \
  -H 'Access-Control-Request-Headers: content-type'
# esperado: Access-Control-Allow-Origin: http://localhost:8080 (e PUT entre os métodos)
```

Com o frontend no ar (§3.1), o mesmo `Access-Control-Allow-Origin` sai do preflight feito contra o
gateway em `http://localhost:8080`, e a CSP do container responde com o endpoint público do bucket em
`img-src`/`connect-src`. O `Origin` autorizado é só uma origem de cada ambiente — os outros dois não se
conhecem (FR-022). Não há mais `MINIO_API_CORS_ALLOW_ORIGIN`: o Spaces aplica a regra **por bucket**, que
é exatamente o que a FR-022 pede, e o desvio registrado no plano desapareceu.

## 9. Configuração inválida (US5)

```bash
cd backend/Ambrosio.API
ObjectStorage__Bucket= dotnet run          # startup falha: "ObjectStorage__Bucket está vazio."
ObjectStorage__PresignedReadMinutes=0 dotnet run   # falha: faixa 1-60
ObjectStorage__AllowedMimeTypes__0=image/tiff dotnet run  # falha: só jpeg/png/webp
```

Nenhuma dessas saídas mostra o valor de `AccessKey`/`SecretKey` (FR-020/FR-021). No Docker, o equivalente
é `docker compose run --rm -e ObjectStorage__Bucket= api`, que deixa o container em *restart loop* com a
mesma mensagem no log.

## 10. Limpeza de envio abandonado (US6)

```bash
A=$(curl -s -X POST $API/images/uploads -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"mimeType":"image/jpeg","sizeBytes":999999,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}')
AID=$(echo $A | jq -r .imageId)
curl -i -X PUT "$(echo $A | jq -r .uploadUrl)" -H 'Content-Type: image/jpeg' --data-binary @$FILE   # enviado, nunca confirmado
sleep 180
curl -s -o /dev/null -w '%{http_code}\n' $API/images/$AID -H "Authorization: Bearer $TOKEN"   # 404
```

Com TTL de 2 min e varredura de 1 min, o `GET` final deve dar **404** em até ~3,5 minutos
(SC-003), e o `DELETE` do objeto no bucket aparece no log de acesso do Spaces. Uma imagem `available`
criada antes permanece. Uma linha `failed` cuja chave foi reaproveitada por um reenvio `available` não
apaga o binário do reenvio (§8 da pesquisa) — ver `Cleanup_ShouldNotDeleteAnObjectBelongingToANewerUpload`.

## 11. Suíte de testes de API (fim do ciclo, constituição §VII)

```bash
ApiBaseUrl=http://localhost:8080/api dotnet test backend/Ambrosio.ApiTests      # homolog
ApiBaseUrl=http://localhost:5000     dotnet test backend/Ambrosio.ApiTests      # Development (§3.2)
```

O projeto tem uma coleção serial (`ApiTests`) para os testes HTTP não competirem pela API em background
com a qual o fixture trabalha. Todos verdes, incluindo os de expiração e limpeza com os prazos curtos.
A suíte inteira já roda contra o Spaces real, que é o que a validação SC-001 pedia.

## 12. CSP do frontend

Em `http://localhost:8080`, o cabeçalho `Content-Security-Policy` inclui
`https://ambrosio.nyc3.digitaloceanspaces.com` (ou o valor de `OBJECT_STORAGE_PUBLIC_ORIGIN`) em
`img-src` e `connect-src`. O config do container é o template
`frontend/nginx/default.conf.template`, e o nginx preenche `${STORAGE_ORIGIN}` no startup
(`NGINX_ENVSUBST_FILTER=STORAGE_ORIGIN`), então cada ambiente aponta para seu storage sem reconstruir a
imagem (FR-023).
