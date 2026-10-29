/**
 * Customer Photo Types — álbum de fotos da cliente (feature 009, data-model §6).
 *
 * Nenhum campo chega com `userId` (contratos/api.md §VI: o dono vem só do claim),
 * e **não existe miniatura**: `url` é o arquivo original (research R3). O campo
 * `imageId` convive com `url` porque a renovação de um link vencido é a rota
 * `GET /images/{imageId}` da feature 004 (research R10).
 */

/** Origem da foto: tela, integração REST ou conversa no ChatGPT (data-model §4). */
export type CustomerPhotoSource = 'web' | 'api' | 'chatGpt'

/** Uma foto do álbum. `url` é o original, nunca uma versão reduzida. */
export interface CustomerPhotoInfo {
  photoId: number
  customerId: number
  /** Imagem do storage (004) que guarda os bytes da foto. */
  imageId: number
  description: string | null
  isFavorite: boolean
  /** A foto é a origem da foto de perfil da cliente (FR-014). */
  isProfile: boolean
  source: CustomerPhotoSource
  /** Instante ISO-8601 UTC. */
  createdAt: string
  updatedAt: string
  /** URL pré-assinada do arquivo original. */
  url: string
  /** Instante em que `url` expira. */
  urlExpiresAt: string
}

/** Página de `GET /customers/{id}/photos` (cursor keyset, research R8). */
export interface CustomerPhotoPage {
  items: CustomerPhotoInfo[]
  /** `photoId` da última foto da página; `null` no fim da lista. */
  nextCursor: number | null
}

/** Parâmetros de `GET /customers/{id}/photos`. */
export interface CustomerPhotoListParams {
  customerId: number
  /** `photoId` da última foto já vista; ausente = primeira página. */
  cursor?: number | null
  /** 1 a 60; padrão `PHOTO_PAGE_SIZE` (24). */
  pageSize?: number
  /** `true` = só favoritas (FR-010). */
  favorites?: boolean
}

/** Corpo de `POST /customers/{id}/photos` (a tela sempre manda `source: 'web'`). */
export interface CustomerPhotoInsertInfo {
  imageId: number
  description?: string | null
  /** `chatGpt` não é aceito pela rota REST (R12) — só `web` ou `api`. */
  source?: 'web' | 'api'
}

/** Resposta da inclusão: `created: false` = a foto já estava no álbum (FR-006). */
export interface CustomerPhotoInsertResult {
  photo: CustomerPhotoInfo
  created: boolean
  /** A mesma imagem já está no álbum de outra cliente da mesma vendedora. */
  alsoInOtherCustomers: boolean
}

/** Corpo de `PUT .../photos/{photoId}/description`. */
export interface CustomerPhotoDescriptionInfo {
  description: string | null
}

/** Corpo de `PUT /customers/{id}/profile-photo`. */
export interface ProfilePhotoUpdateInfo {
  photoId: number
  /** Recorte quadrado de até 512 × 512 gerado no navegador (R4); ausente = usa a foto. */
  imageId?: number
}

/**
 * Foto de perfil da cliente, embutida em `CustomerInfo`/`CustomerDetailInfo`.
 * `imageId` é o recorte do navegador (ou a própria foto, quando o perfil veio de
 * um canal sem navegador); `originalImageId` é a imagem do álbum (FR-014).
 * Na LISTAGEM `originalUrl` vem nulo de propósito: uma URL assinada por linha.
 */
export interface CustomerProfilePhotoInfo {
  photoId: number
  imageId: number
  originalImageId: number
  /** URL do recorte quadrado exibido no círculo. */
  url: string
  originalUrl: string | null
  urlExpiresAt: string
}

/** Estado de um item da fila de envio da tela (research R7/R9). */
export type PhotoUploadState =
  /** `POST /images` em voo. */
  | 'uploading'
  /** `POST .../photos` em voo. */
  | 'attaching'
  /** Concluído: a célula provisória já foi trocada pela foto da resposta. */
  | 'done'
  /** Falhou; `error` traz o motivo. */
  | 'error'

/** Um arquivo na fila de envio, com a miniatura local e o estado do par de chamadas. */
export interface PhotoUploadItem {
  /** Id da fila (não é id de foto nem de imagem). */
  localId: string
  fileName: string
  /** `URL.createObjectURL(file)` — revogado quando o item sai da fila. */
  previewUrl: string
  file: File
  state: PhotoUploadState
  /** Mensagem legível quando `state === 'error'`. */
  error: string | null
}

/** Página da grade (3 colunas × 8 linhas), como em `contracts/ui.md` §2. */
export const PHOTO_PAGE_SIZE = 24

/** Limite da célula inicial da grade antes de qualquer rolagem (9 = 3×3). */
export const PHOTO_GRID_SKELETON_CELLS = 9

/** Tipos aceitos — os mesmos de `ObjectStorage:AllowedMimeTypes` na 004. */
export const ACCEPTED_PHOTO_MIME_TYPES = ['image/jpeg', 'image/png', 'image/webp'] as const

/** Teto de `POST /images` (`ObjectStorage:MaxUploadBytes` padrão, 10 MiB). */
export const MAX_PHOTO_BYTES = 10_485_760

/** Máximo de `description` (FR-007); o mesmo valor do validator da API. */
export const MAX_PHOTO_DESCRIPTION_LENGTH = 500

/** Envios simultâneos na fila (research R7). */
export const PHOTO_UPLOAD_CONCURRENCY = 3

/** Folga de renovação: relê a página quando faltar menos que isto para vencer. */
export const PHOTO_URL_REFRESH_MARGIN_MS = 30_000

/** `true` quando o tipo declarado está na lista aceita pela 004. */
export const isAcceptedPhotoType = (mimeType: string): boolean =>
  (ACCEPTED_PHOTO_MIME_TYPES as readonly string[]).includes(mimeType)
