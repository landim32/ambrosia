# Contract: Images API

**Feature**: `004-s3-image-storage` | **Spec**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Todas as rotas exigem `Authorization: Bearer <token>` (constituição §VI); sem token → **401**. O dono é sempre o `sub` do token. Imagem de outro usuário → **404**, com a mesma mensagem de inexistente. Erros usam `ErrorResult` (`sucesso`, `mensagem`, `erros`). Storage indisponível → **503** `{ "sucesso": false, "mensagem": "Armazenamento de imagens indisponível.", "erros": [] }`.

Atrás do gateway, as rotas ficam em `/api/images/...`.

---

## `POST /images/uploads` — declarar envio pelo cliente

**Request**

```json
{ "mimeType": "image/jpeg", "sizeBytes": 245812, "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08" }
```

**201 Created**: imagem nova, ou pendente do mesmo hash reaproveitada com URL nova.

```json
{
  "imageId": 42,
  "alreadyExists": false,
  "uploadUrl": "https://ambrosia-prod.nyc3.digitaloceanspaces.com/users/7/images/9f86...a08.jpg?X-Amz-Algorithm=...&X-Amz-Signature=...",
  "uploadMethod": "PUT",
  "contentType": "image/jpeg",
  "uploadExpiresAt": "2026-10-26T15:05:00Z",
  "image": null
}
```

**200 OK**: já existe imagem `available` do usuário com esse hash. Nenhum objeto ou registro novo.

```json
{ "imageId": 40, "alreadyExists": true, "uploadUrl": null, "uploadMethod": null, "contentType": null, "uploadExpiresAt": null, "image": { "...": "ImageInfo" } }
```

**400**: `mimeType` fora da lista, `sizeBytes` ≤ 0 ou acima do limite, `sha256` mal formado. `erros[]` indica o campo.

### Envio do binário (direto ao storage, fora da API)

```http
PUT {uploadUrl}
Content-Type: {contentType}

<bytes>
```

- `Content-Type` diferente do assinado → **403** do storage.
- Depois de `uploadExpiresAt` → **403** do storage.

---

## `POST /images/uploads/{imageId}/confirm` — confirmar envio

Sem corpo.

**200 OK**: `ImageInfo` com `status: "available"`, `url` e `urlExpiresAt`. Repetir a confirmação de uma imagem já disponível devolve o mesmo, com URL nova.

**400**: com `mensagem` específica:

| Situação | `mensagem` | Efeito |
|---|---|---|
| Objeto ainda não enviado | "O arquivo da imagem ainda não foi enviado." | continua `pending` |
| Tamanho real ≠ declarado ou > limite | "O tamanho do arquivo enviado não confere com o declarado ou excede o limite." | objeto apagado, `failed` |
| Tipo real não reconhecido, fora da lista ou ≠ declarado | "O conteúdo do arquivo não é uma imagem de tipo aceito." | objeto apagado, `failed` |
| Hash ≠ declarado | "O conteúdo do arquivo não confere com o hash declarado." | objeto apagado, `failed` |
| Imagem já `failed` | "Este envio foi recusado. Declare um novo envio." | — |

**404**: imagem inexistente ou de outro usuário.

---

## `POST /images` — gravar pelo servidor

`Content-Type: multipart/form-data`, campo `file`. Limite: `MaxUploadBytes` (padrão 10 MB).

O servidor detecta o tipo pelo conteúdo, corrige a orientação, remove EXIF/IPTC/XMP, re-codifica, calcula o hash do conteúdo final e aplica a deduplicação. `source` = `server`.

**201 Created**: `ImageInfo` (`status: "available"`, com `url`).
**200 OK**: mesmo conteúdo final já disponível para o usuário, devolve a imagem existente.
**400**: arquivo ausente ou vazio, tipo não aceito, tamanho acima do limite.
**413**: corpo acima do limite de requisição.

---

## `GET /images/{imageId}` — consultar

**200 OK**: `ImageInfo`

```json
{
  "imageId": 42,
  "mimeType": "image/jpeg",
  "sizeBytes": 245812,
  "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
  "width": 1200,
  "height": 1600,
  "status": "available",
  "source": "upload",
  "url": "https://...?X-Amz-...",
  "urlExpiresAt": "2026-10-26T15:15:00Z",
  "createdAt": "2026-10-26T15:00:00Z"
}
```

Com `status` `pending` ou `failed`, `url` e `urlExpiresAt` vêm `null` (FR-015).

**404**: inexistente ou de outro usuário.

---

## `DELETE /images/{imageId}` — excluir

**204 No Content**: objeto apagado do storage (se existia) e metadado removido.
**404**: inexistente ou de outro usuário. A imagem de outro usuário fica intacta.

---

## Storage (comportamento verificado pelos testes de API)

| Ação | Resposta esperada do storage |
|---|---|
| `GET` no endereço do objeto sem query string | 403 |
| `GET {url}` antes de `urlExpiresAt` | 200 com o binário |
| `GET {url}` depois de `urlExpiresAt` | 403 |
| `GET {url}` obtida antes da exclusão/recusa, ainda no prazo | 404 (`NoSuchKey`) |
| Preflight `OPTIONS` vindo da origem do frontend | 200 com `Access-Control-Allow-Origin` |

## Operações internas (sem rota)

- `IImageService.DeleteAllForUserAsync(userId)`: remove todos os objetos sob `users/{userId}/` e os metadados (FR-017).
- `IImageService.PurgeStaleAsync(now)`: chamada pelo job de limpeza (FR-018).
