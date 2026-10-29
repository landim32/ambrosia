# Contrato de API: Fotos de clientes

**Feature**: `009-customer-photos` | **Revisão**: 2026-10-29 (sem processamento no servidor —
ver [research.md](research.md))

Prefixo `/customers/{customerId}/…` sob autenticação JWT (§VI). `customerId` vem da path; `userId` só do
claim `sub`. Convenções da casa: `Result<T>` com `sucesso`/`mensagem`/`erros`, enums camelCase como string,
datas ISO-8601 UTC, **404** para inexistente **e** para recurso de outra vendedora (indistinguíveis,
FR-025/SC-006), sem cascata no banco.

**Nenhuma rota desta feature recebe bytes de imagem.** O arquivo entra pelas rotas de imagem já publicadas
da feature 004 (`POST /images` multipart ou o fluxo pré-assinado), e as rotas de fotos só relacionam
`imageId` ↔ cliente. É o que mantém a validação de upload da 004 intacta (R2/R7) e o que dispensa
biblioteca de decodificação no servidor.

---

## 1. Listar fotos — `GET /customers/{customerId}/photos`

Query: `cursor` (opcional, `photoId` da última foto da página anterior), `pageSize` (1–60, padrão 24),
`favorites` (`true` = só favoritas).

200 OK — `CustomerPhotoPage`:

```json
{
  "items": [
    {
      "photoId": 1204,
      "customerId": 88,
      "imageId": 5731,
      "description": "provando o vestido azul, ficou ótimo",
      "isFavorite": true,
      "isProfile": true,
      "source": "web",
      "createdAt": "2026-10-29T10:00:00Z",
      "updatedAt": "2026-10-29T10:12:00Z",
      "url": "https://spaces…/5731.jpg?X-Amz-…",
      "urlExpiresAt": "2026-10-29T10:15:00Z"
    }
  ],
  "nextCursor": 1180
}
```

- Ordem: `created_at DESC, customer_photo_id DESC`. `nextCursor` nulo no fim da lista (R8).
- `url` é a do **arquivo original** — não existe miniatura (R3). `imageId` é o que a tela usa para renovar
  a URL por `GET /images/{imageId}` (R10).
- `isProfile` = a foto é a origem da foto de perfil da cliente.
- `url`/`urlExpiresAt` presentes porque a foto só existe com a imagem `available` (o serviço recusa anexar
  imagem pendente/falha — §4).
- 404 se a cliente não existe ou é de outra vendedora.

## 2. Anexar foto ao álbum — `POST /customers/{customerId}/photos`

Body: `{ "imageId": 5731, "description"?: "…", "source"?: "web" | "api" }` (padrão `api`; o frontend manda
`web`; `chatGpt` não é aceito aqui — R12).

201 Created — `CustomerPhotoInsertResult`:

```json
{
  "photo": { "photoId": 1204, "imageId": 5731, "isFavorite": false, "isProfile": false, "url": "…", "urlExpiresAt": "…" },
  "created": true,
  "alsoInOtherCustomers": false
}
```

- Mesma imagem já anexada **a esta cliente** → `200` com `created: false` e `photo` = existente (FR-006).
  A distinção 201/200 é pelo `Location` de foto nova (a 004 usa o mesmo critério em `POST /images/uploads`).
- `alsoInOtherCustomers: true` quando o mesmo `stored_image` já está no álbum de outra cliente da mesma
  vendedora (edge case "mesma foto em duas clientes").
- `description` é **sempre escrita como veio** (ou `null` se vazia): anexar de uma imagem já existente não
  sobrescreve a descrição da foto anterior — a corrida de `DuplicateException` relê a existente e devolve,
  sem tocar nela (a semântica é "esta foto já está no álbum", não "atualize a legenda").
- `imageId` inexistente, de outra vendedora, ou que não está `available` → 404/400 (§4).
- `RequestSizeLimit`: não se aplica — nenhum byte passa por aqui. O limite de bytes continua sendo o de
  `POST /images` (10 MB).

## 3. Editar descrição — `PUT /customers/{customerId}/photos/{photoId}/description`

Body `{ "description": "…" }` (opcional; vazio ou só espaços → `null`). 200 → foto atualizada.
`description` com mais de 500 caracteres → 400 `erros: [{ campo: "description", mensagem: "A descrição deve ter no máximo 500 caracteres." }]` (FR-007, cenário US4-5).

## 4. Favorita — `PUT` / `DELETE /customers/{customerId}/photos/{photoId}/favorite`

`PUT` marca, `DELETE` desmarca. 200 → foto atualizada. Idempotente (refazer não muda `updatedAt`).
Existe uma operação para cada verbo porque o MCP exige paridade 1:1 (R11).

## 5. Excluir foto — `DELETE /customers/{customerId}/photos/{photoId}` → 204

- Se a foto é a origem do perfil → o perfil é removido (`profile_photo_id` e `profile_image_id` a nulo), e a
  cliente volta às iniciais (FR-015, cenário US3-6, edge case).
- O arquivo some do storage **se ficou órfão**: nenhuma outra `customer_photo`, nenhum
  `customers.profile_image_id` e nenhum `order_items.image_id` apontando para ele (FR-009, R6). Referência
  em `order_items` é o caso real: a mesma imagem pode ser foto de cliente **e** imagem de item de pedido
  importado, e excluir do álbum não pode quebrar o pedido.
- 404 se a foto não existe ou é de outra vendedora.

## 6. Definir foto de perfil — `PUT /customers/{customerId}/profile-photo`

Body `ProfilePhotoUpdateInfo`:

```json
{ "photoId": 1204, "imageId": 5740 }
```

- `photoId` — foto do álbum de onde o perfil saiu (obrigatório; FR-011/FR-014). Precisa ser foto **desta**
  cliente → 404 se não for.
- `imageId` — **opcional**: o recorte quadrado de até 512 × 512 gerado no navegador e subido por
  `POST /images` (R4). Sem ele, o círculo usa o arquivo da própria foto (caso do ChatGPT, que não tem
  canvas). Quando um perfil anterior tinha recorte, o recorte velho vira candidato a órfão.
- `imageId` precisa ser `available` e do usuário → 400/404 (§4 do quadro de erros).

200 OK — `CustomerProfilePhotoInfo`:
`{ "photoId": 1204, "imageId": 5740, "originalImageId": 5731, "url": "…", "originalUrl": "…", "urlExpiresAt": "…" }`

## 7. Remover foto de perfil — `DELETE /customers/{customerId}/profile-photo` → 204

A foto continua no álbum (FR-015). Se havia recorte dedicado, vira candidato a órfão.

## 8. Fotos avulsa

**Não existe rota** `GET /customers/{customerId}/photos/{photoId}`. A renovação de URL de uma foto isolada é
`GET /images/{imageId}` (004, já publicada e testada) — R10. É o que mantém o `ParityTests` sem exceção
nova.

## 9. Campos novos nas rotas de cliente existentes

`CustomerInfo` ganha `profilePhoto?: CustomerProfilePhotoInfo` (na listagem, sem `originalUrl`), e
`CustomerDetailInfo` ganha `photoCount`. `OrderDetailInfo.customer` (que é um `CustomerInfo`) passa a trazer
o avatar, para o círculo na tela do pedido (FR-016).

## 10. Erros (formato `Result<T>`, §IV)

| Condição | HTTP | `mensagem` |
|---|---|---|
| Cliente inexistente ou de outra vendedora | 404 | `Cliente não encontrado.` (a mesma frase da 001/007 — um 404 por cliente não pode ter duas redações na API) |
| Foto inexistente ou de outra vendedora | 404 | `Foto não encontrada.` |
| `imageId` inexistente ou de outra vendedora | 404 | `Imagem não encontrada.` |
| `imageId` existe mas está `pending`/`failed` | 400 | `A imagem ainda não está disponível.` |
| Descrição acima de 500 caracteres | 400 | `Dados inválidos.` + `erros[0].campo = "description"` |
| `pageSize` fora de 1–60 | 400 | `Dados inválidos.` + `erros[0].campo = "pageSize"` |
| Recusa de upload (byte/tipo/hash) | — | acontece em `POST /images`/confirmar, com as mensagens da 004 — esta feature não cria mensagem de recusa de tipo |
| Falha inesperada | 500 | `Erro interno ao processar a solicitação.` |

### Erro mapeado no controller — detalhe de implementação

Nenhuma rota de foto faz upload multipart, então **não há `ImageCouldNotDecode`** nem `Dimensions` aqui. O
mapeamento que estas rotas precisam é: `ValidationException`→400, `NotFoundException`→404,
`DuplicateException`→tratado como "já existe" (200), `StorageUnavailableException`→503 e qualquer outra→500
com log de `customerId`/`photoId`/`userId` e **sem** URL assinada no log (FR-021).
