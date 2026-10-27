/**
 * Imagem do storage como `GET /images/{id}` devolve (feature 004, `images-api.md`).
 * Só os campos que a tela usa: a URL de leitura é pré-assinada, gerada a cada
 * resposta, e só existe com a imagem `available`.
 */
export interface ImageInfo {
  imageId: number
  mimeType: string
  status: string
  /** URL pré-assinada de leitura; `null` fora de `available`. */
  url: string | null
  /** Instante UTC em que `url` expira. */
  urlExpiresAt: string | null
}
