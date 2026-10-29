import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { writeSession } from '@/Services/authStorage'
import { imageService } from '@/Services/imageService'

import type { AuthSession } from '@/types/auth'

/**
 * `imageService.upload` (T027) é a única porta de bytes da tela: a foto do
 * álbum e o recorte quadrado do navegador entram pela rota `POST /images` da
 * feature 004, e nada nesta feature cria rota de upload nova (research R7).
 *
 * O que o teste garante é o detalhe que costuma quebrar: o corpo vai como
 * `FormData` cru, SEM `Content-Type` próprio — o boundary é do navegador.
 */
interface RecordedCall {
  url: string
  init: RequestInit
}

const calls: RecordedCall[] = []

const jsonResponse = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })

const mockFetch = (factory: () => Response) =>
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    calls.push({
      url: typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
      init: init ?? {},
    })

    return Promise.resolve(factory())
  })

const lastCall = (): RecordedCall => {
  const last = calls.at(-1)
  if (!last) throw new Error('fetch nao foi chamado')

  return last
}

const SESSION: AuthSession = {
  token: 'jwt-do-servico-teste',
  expiresAt: '2099-01-01T00:00:00Z',
  user: {
    id: 11,
    email: 'ana@exemplo.com',
    name: 'Ana',
    provider: 'password',
    createdAt: '2026-01-01T00:00:00Z',
  },
}

const imageInfo = {
  imageId: 5740,
  mimeType: 'image/webp',
  status: 'available',
  url: 'https://storage.example/5740.webp',
  urlExpiresAt: '2099-01-01T00:00:00Z',
}

describe('imageService.upload', () => {
  beforeEach(() => {
    calls.length = 0
    localStorage.clear()
    writeSession(SESSION)
    vi.stubEnv('VITE_API_URL', '/api')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.restoreAllMocks()
    localStorage.clear()
  })

  it('manda o arquivo por multipart sem Content-Type proprio', async () => {
    mockFetch(() => jsonResponse(201, imageInfo))

    const file = new File(['bytes'], 'vestido.jpg', { type: 'image/jpeg' })

    const result = await imageService.upload(file)

    expect(lastCall().url).toBe('/api/images')
    expect(lastCall().init.method).toBe('POST')
    expect(lastCall().init.body).toBeInstanceOf(FormData)
    expect(new Headers(lastCall().init.headers).get('content-type')).toBeNull()
    expect(new Headers(lastCall().init.headers).get('authorization')).toBe(
      `Bearer ${SESSION.token}`,
    )
    expect(result.imageId).toBe(5740)
  })

  it('o campo chama file, como espera o ImagesController', async () => {
    mockFetch(() => jsonResponse(201, imageInfo))

    await imageService.upload(new File(['x'], 'a.png', { type: 'image/png' }))

    const body = lastCall().init.body as FormData

    expect(body.get('file')).toBeInstanceOf(File)
  })

  /** O recorte sai do canvas como `Blob` solto: ai o nome vem por parametro. */
  it('da nome ao blob do recorte', async () => {
    mockFetch(() => jsonResponse(201, imageInfo))

    await imageService.upload(new Blob(['x'], { type: 'image/webp' }), 'foto-perfil.webp')

    const body = lastCall().init.body as FormData
    const entry = body.get('file') as File

    expect(entry.name).toBe('foto-perfil.webp')
  })

  /** A recusa de tipo/tamanho e da 004: a mensagem dela chega como ApiError. */
  it('propaga a recusa da 004', async () => {
    mockFetch(() =>
      jsonResponse(400, {
        sucesso: false,
        mensagem: 'Dados inválidos.',
        erros: [{ campo: 'mimeType', mensagem: 'Tipo não aceito.' }],
      }),
    )

    await expect(
      imageService.upload(new File(['x'], 'nota.pdf', { type: 'application/pdf' })),
    ).rejects.toThrow('Dados inválidos.')
  })

  /** R10: a renovacao de link vencido e a rota da 004, com cache por imageId. */
  it('getReadUrl cacheia por imageId e divide a chamada em voo', async () => {
    mockFetch(() => jsonResponse(200, imageInfo))

    const [a, b] = await Promise.all([
      imageService.getReadUrl(900_001),
      imageService.getReadUrl(900_001),
    ])

    expect(a).toBe('https://storage.example/5740.webp')
    expect(b).toBe(a)
    expect(calls.filter((call) => call.url === '/api/images/900001')).toHaveLength(1)
  })
})
