import type { Area } from 'react-easy-crop'

/**
 * Recorte quadrado da foto de perfil, feito **no navegador** (feature 009,
 * research R4): o `<canvas>` recebe o bitmap do `<img>` já orientado pelo
 * navegador (R2) e `toBlob` produz o arquivo que sobe por `POST /images`. O
 * servidor não decodifica nada — por isso este arquivo existe no frontend.
 */

/** Tamanho do lado do recorte: cobre o avatar maior (128 px) em telas 4×. */
export const PROFILE_CROP_SIZE = 512

/** Formato pedido ao navegador; o mesmo trio aceito pela feature 004. */
export const PROFILE_CROP_MIME_TYPE = 'image/webp'

export type CropToFileReason =
  /** jsdom, navegador antigo ou canvas bloqueado: `getContext('2d')` devolve `null`. */
  | 'no-canvas'
  /** O navegador não implementa `HTMLCanvasElement.toBlob`. */
  | 'no-toblob'
  /** A imagem ainda não terminou de carregar (lado natural 0). */
  | 'not-loaded'
  /** `toBlob` chamou o callback com `null` (codificador indisponível). */
  | 'empty'
  /** Canvas contaminado: o storage não devolve CORS e o navegador recusa ler os pixels. */
  | 'security'
  /** Área de recorte sem tamanho útil. */
  | 'bad-area'

/** Erro do recorte com o motivo, para a tela escolher a frase (nenhuma string aqui). */
export class CropToFileError extends Error {
  readonly reason: CropToFileReason

  constructor(reason: CropToFileReason) {
    super(reason)
    this.name = 'CropToFileError'
    this.reason = reason
  }
}

const clamp = (value: number, min: number, max: number): number =>
  Math.min(Math.max(value, min), max)

/**
 * Recorta `area` (pixels da imagem exibida, no espaço do `<img>` já orientado) e
 * devolve um `File` quadrado de `size × size`.
 *
 * A área é cortada nos limites da imagem antes de desenhar: `react-easy-crop`
 * já restringe o quadrado, mas uma leitura fora do ar ou um `zoom` no meio do
 * salvamento pode entregar coordenada parcial — e `drawImage` com fonte fora da
 * imagem desenha vazio, sem erro.
 */
export const cropToFile = async (
  image: HTMLImageElement,
  area: Area,
  size: number = PROFILE_CROP_SIZE,
  mimeType: string = PROFILE_CROP_MIME_TYPE,
  fileName = 'foto-perfil.webp',
): Promise<File> => {
  const naturalWidth = image.naturalWidth
  const naturalHeight = image.naturalHeight

  if (naturalWidth <= 0 || naturalHeight <= 0) throw new CropToFileError('not-loaded')

  const x = clamp(Math.round(area.x), 0, naturalWidth)
  const y = clamp(Math.round(area.y), 0, naturalHeight)
  const width = clamp(Math.round(area.width), 0, naturalWidth - x)
  const height = clamp(Math.round(area.height), 0, naturalHeight - y)

  if (width <= 0 || height <= 0) throw new CropToFileError('bad-area')

  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size

  const context = canvas.getContext('2d')
  if (context === null) throw new CropToFileError('no-canvas')

  context.drawImage(image, x, y, width, height, 0, 0, size, size)

  if (typeof canvas.toBlob !== 'function') throw new CropToFileError('no-toblob')

  type ToBlobOutcome = { readonly failed: false; readonly blob: Blob | null } | { readonly failed: true }

  const outcome = await new Promise<ToBlobOutcome>((resolve) => {
    try {
      canvas.toBlob((blob) => {
        resolve({ failed: false, blob })
      }, mimeType, 0.9)
    } catch {
      // Canvas contaminado (imagem de outra origem sem CORS): o navegador lança
      // SecurityError, e não dá para ler os pixels de jeito nenhum.
      resolve({ failed: true })
    }
  })

  if (outcome.failed) throw new CropToFileError('security')
  if (outcome.blob === null) throw new CropToFileError('empty')

  const blob = outcome.blob

  return new File([blob], fileName, { type: blob.type !== '' ? blob.type : mimeType })
}
