import { apiRequest } from '@/Services/apiClient'

import type { ImageInfo } from '@/types/image'

/** Folga antes do vencimento: uma URL quase vencida não é reaproveitada. */
const EXPIRY_MARGIN_MS = 10_000

/**
 * Image Service — URL de leitura das fotos do storage (feature 004/006, skill
 * `react-architecture`: classe singleton sobre `apiRequest`).
 *
 * A foto importada do +Vendas fica no bucket privado; o item do pedido traz só o
 * `imageId`. A URL pré-assinada vem de `GET /images/{id}` e vale poucos minutos,
 * então fica em cache por `imageId` até perto de `urlExpiresAt`. O mesmo SKU em
 * vários itens (mesma imagem) faz uma chamada só; chamadas simultâneas dividem a
 * mesma promessa.
 */
class ImageService {
  private readonly cache = new Map<number, { url: string | null; validUntil: number }>()

  private readonly pending = new Map<number, Promise<string | null>>()

  /** `GET /images/{id}`. */
  async getById(imageId: number): Promise<ImageInfo> {
    return apiRequest<ImageInfo>('GET', `/images/${String(imageId)}`)
  }

  /** URL de leitura da imagem, do cache enquanto vale; `null` quando não há. */
  async getReadUrl(imageId: number): Promise<string | null> {
    const cached = this.cache.get(imageId)
    if (cached && cached.validUntil > Date.now()) return cached.url

    const inFlight = this.pending.get(imageId)
    if (inFlight) return inFlight

    const request = this.getById(imageId)
      .then((image) => {
        const expiresAt = image.urlExpiresAt ? Date.parse(image.urlExpiresAt) : Number.NaN
        if (image.url && !Number.isNaN(expiresAt)) {
          this.cache.set(imageId, { url: image.url, validUntil: expiresAt - EXPIRY_MARGIN_MS })
        }
        return image.url
      })
      .finally(() => {
        this.pending.delete(imageId)
      })

    this.pending.set(imageId, request)
    return request
  }
}

export const imageService = new ImageService()
export default ImageService
