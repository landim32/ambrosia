import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CropToFileError, cropToFile } from '@/lib/cropToFile'

import type { Area } from 'react-easy-crop'

/**
 * Recorte do perfil feito no navegador (feature 009, T043, research R4).
 *
 * O jsdom NÃO implementa canvas: `getContext('2d')` devolve `null` e
 * `toBlob` não existe. Por isso o teste stuba os dois explicitamente — é o
 * caminho que o navegador percorre de verdade, e o teste asserting do que a
 * função faz com eles (lado desenhado, formato pedido), além das recusas.
 */
const AREA: Area = { x: 40, y: 20, width: 300, height: 300 }

const imageOf = (naturalWidth: number, naturalHeight: number): HTMLImageElement =>
  ({ naturalWidth, naturalHeight } as unknown as HTMLImageElement)

interface CanvasStub {
  drawImage: ReturnType<typeof vi.fn>
  toBlob: ReturnType<typeof vi.fn>
  captured: { width: number; height: number } | null
}

const stubCanvas = ({ context = true, blob = true }: { context?: boolean; blob?: boolean } = {}): CanvasStub => {
  const drawImage = vi.fn()
  const toBlob = vi.fn(
    (cb: (b: Blob | null) => void, type?: string) => {
      // O navegador devolve o blob com o mime que foi pedido.
      cb(blob ? new Blob(['bytes'], { type: type ?? 'image/webp' }) : null)
    },
  )

  const stub: CanvasStub = {
    drawImage,
    toBlob,
    captured: null,
  }

  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
    function (this: HTMLCanvasElement) {
      if (!context) return null

      stub.captured = { width: this.width, height: this.height }

      return { drawImage } as unknown as CanvasRenderingContext2D
    },
  )

  Object.defineProperty(HTMLCanvasElement.prototype, 'toBlob', {
    // `blob: false` reproduz o navegador sem o método (é o que o jsdom é).
    value: blob ? toBlob : undefined,
    configurable: true,
    writable: true,
  })

  return stub
}

const capture = async (promise: Promise<unknown>): Promise<unknown> =>
  promise.then(
    () => {
      throw new Error('a chamada deveria ter falhado')
    },
    (error: unknown) => error,
  )

describe('cropToFile', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()

    delete (HTMLCanvasElement.prototype as unknown as { toBlob?: unknown }).toBlob
  })

  it('desenha a área escolhida num canvas quadrado de 512 e devolve o arquivo', async () => {
    const canvas = stubCanvas()

    const file = await cropToFile(imageOf(600, 400), AREA)

    expect(canvas.drawImage).toHaveBeenCalledWith(
      expect.objectContaining({ naturalWidth: 600 }),
      40,
      20,
      300,
      300,
      0,
      0,
      512,
      512,
    )
    expect(canvas.captured).toEqual({ width: 512, height: 512 })
    expect(file).toBeInstanceOf(File)
    expect(file.type).toBe('image/webp')
    expect(file.name).toBe('foto-perfil.webp')
  })

  it('pede o formato e o tamanho que o chamador escolher', async () => {
    const canvas = stubCanvas()

    const file = await cropToFile(imageOf(600, 400), AREA, 256, 'image/jpeg', 'recorte.jpg')

    expect(canvas.captured).toEqual({ width: 256, height: 256 })
    expect(file.type).toBe('image/jpeg')
    expect(file.name).toBe('recorte.jpg')
  })

  /** `toBlob` é o que o jsdom não tem: a recusa precisa ser um erro próprio. */
  it('recusa quando o navegador não implementa toBlob', async () => {
    stubCanvas({ blob: false })

    // `blob: false` define `toBlob` como `undefined`, que é exatamente o estado
    // de um navegador sem o método.
    const error = await capture(cropToFile(imageOf(600, 400), AREA))

    expect(error).toBeInstanceOf(CropToFileError)
    expect((error as CropToFileError).reason).toBe('no-toblob')
  })

  it('recusa quando o contexto 2d não existe', async () => {
    stubCanvas({ context: false })

    const error = await capture(cropToFile(imageOf(600, 400), AREA))

    expect(error).toBeInstanceOf(CropToFileError)
    expect((error as CropToFileError).reason).toBe('no-canvas')
  })

  it('recusa quando o codificador devolve blob nulo', async () => {
    stubCanvas()

    // Canvas presente, `toBlob` chama o callback com `null`: codificador indisponível.
    Object.defineProperty(HTMLCanvasElement.prototype, 'toBlob', {
      value: (cb: (b: Blob | null) => void) => {
        cb(null)
      },
      configurable: true,
      writable: true,
    })

    const error = await capture(cropToFile(imageOf(600, 400), AREA))

    expect(error).toBeInstanceOf(CropToFileError)
    expect((error as CropToFileError).reason).toBe('empty')
  })

  it('recusa imagem ainda sem carregar', async () => {
    stubCanvas()

    const error = await capture(cropToFile(imageOf(0, 0), AREA))

    expect(error).toBeInstanceOf(CropToFileError)
    expect((error as CropToFileError).reason).toBe('not-loaded')
  })

  it('recusa área sem tamanho útil', async () => {
    stubCanvas()

    const error = await capture(cropToFile(imageOf(600, 400), { x: 0, y: 0, width: 0, height: 0 }))

    expect(error).toBeInstanceOf(CropToFileError)
    expect((error as CropToFileError).reason).toBe('bad-area')
  })

  /** A área é cortada nos limites do arquivo: coordenada fora não desenha vazio. */
  it('corta a área fora dos limites da imagem', async () => {
    const canvas = stubCanvas()

    await cropToFile(imageOf(600, 400), { x: 500, y: 350, width: 400, height: 400 })

    expect(canvas.drawImage).toHaveBeenCalledWith(
      expect.anything(),
      500,
      350,
      100,
      50,
      0,
      0,
      512,
      512,
    )
  })

  /** Canvas contaminado (imagem de outra origem sem CORS): `toBlob` lança. */
  it('recusa quando o canvas está contaminado', async () => {
    stubCanvas()

    Object.defineProperty(HTMLCanvasElement.prototype, 'toBlob', {
      value: () => {
        throw new Error('SecurityError')
      },
      configurable: true,
      writable: true,
    })

    const error = await capture(cropToFile(imageOf(600, 400), AREA))

    expect(error).toBeInstanceOf(CropToFileError)
    expect((error as CropToFileError).reason).toBe('security')
  })
})
