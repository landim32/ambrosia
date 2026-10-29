import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError, NotFoundError, UnauthorizedError, setUnauthorizedHandler } from '@/Services/apiClient'
import { writeSession } from '@/Services/authStorage'
import { customerPhotoService } from '@/Services/customerPhotoService'

import type { AuthSession } from '@/types/auth'
import type { CustomerPhotoInfo } from '@/types/customerPhoto'

/**
 * customerPhotoService (T029): a MONTAGEM da chamada — rota, cursor, `favorites`
 * e `pageSize=24` — e a propagação das classes do transporte. O mock é só em
 * `fetch` (mesma regra de `customerService.test.ts`: o `apiClient` já tem suíte
 * própria, e o que se testa aqui é o que este serviço decide).
 */
interface RecordedCall {
  url: string
  init: RequestInit
}

const calls: RecordedCall[] = []

const record = (input: RequestInfo | URL, init: RequestInit | undefined): void => {
  calls.push({
    url: typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
    init: init ?? {},
  })
}

const jsonResponse = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })

const mockFetch = (factory: () => Response) =>
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    record(input, init)

    return Promise.resolve(factory())
  })

const lastUrl = (): string => {
  const last = calls.at(-1)
  if (!last) throw new Error('fetch nao foi chamado')

  return last.url
}

const lastBody = (): string => {
  const last = calls.at(-1)
  if (!last) throw new Error('fetch nao foi chamado')

  return typeof last.init.body === 'string' ? last.init.body : ''
}

const lastHeaders = (): Headers => {
  const last = calls.at(-1)
  if (!last) throw new Error('fetch nao foi chamado')

  return new Headers(last.init.headers)
}

const capture = async (promise: Promise<unknown>): Promise<unknown> =>
  promise.then(
    () => {
      throw new Error('a requisicao deveria ter falhado')
    },
    (error: unknown) => error,
  )

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

const photo = (overrides: Partial<CustomerPhotoInfo> = {}): CustomerPhotoInfo => ({
  photoId: 1204,
  customerId: 88,
  imageId: 5731,
  description: null,
  isFavorite: false,
  isProfile: false,
  source: 'web',
  createdAt: '2026-10-29T10:00:00Z',
  updatedAt: '2026-10-29T10:00:00Z',
  url: 'https://storage.example/5731.jpg?sig=1',
  urlExpiresAt: '2026-10-29T10:15:00Z',
  ...overrides,
})

describe('customerPhotoService', () => {
  beforeEach(() => {
    calls.length = 0
    setUnauthorizedHandler(undefined)
    localStorage.clear()
    writeSession(SESSION)
    vi.stubEnv('VITE_API_URL', '/api')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.restoreAllMocks()
    setUnauthorizedHandler(undefined)
    localStorage.clear()
  })

  it('lista a primeira pagina com pageSize=24 e sem favorites', async () => {
    mockFetch(() => jsonResponse(200, { items: [photo()], nextCursor: 1180 }))

    const page = await customerPhotoService.list({ customerId: 88 })

    expect(lastUrl()).toBe('/api/customers/88/photos?pageSize=24')
    expect(page.items[0]?.photoId).toBe(1204)
    expect(page.nextCursor).toBe(1180)
  })

  it('monta cursor e favorites=true', async () => {
    mockFetch(() => jsonResponse(200, { items: [], nextCursor: null }))

    await customerPhotoService.list({ customerId: 88, cursor: 1180, favorites: true })

    expect(lastUrl()).toBe('/api/customers/88/photos?cursor=1180&pageSize=24&favorites=true')
  })

  it('aceita pageSize diferente e nao manda cursor nulo', async () => {
    mockFetch(() => jsonResponse(200, { items: [], nextCursor: null }))

    await customerPhotoService.list({ customerId: 88, cursor: 0, pageSize: 60, favorites: false })

    expect(lastUrl()).toBe('/api/customers/88/photos?pageSize=60')
  })

  /** R7: os bytes NAO passam por aqui — o corpo e so o imageId. */
  it('anexa com imageId, source=web e sem descricao', async () => {
    mockFetch(() =>
      jsonResponse(201, { photo: photo(), created: true, alsoInOtherCustomers: false }),
    )

    const result = await customerPhotoService.attach(88, 5731)

    expect(lastUrl()).toBe('/api/customers/88/photos')
    expect(JSON.parse(lastBody())).toEqual({ imageId: 5731, source: 'web' })
    expect(lastHeaders().get('content-type')).toBe('application/json')
    expect(result.created).toBe(true)
  })

  it('anexa com descricao quando a chamador deu uma', async () => {
    mockFetch(() =>
      jsonResponse(201, { photo: photo({ description: 'vestido azul' }), created: true, alsoInOtherCustomers: false }),
    )

    await customerPhotoService.attach(88, 5731, 'vestido azul')

    expect(JSON.parse(lastBody())).toEqual({
      imageId: 5731,
      description: 'vestido azul',
      source: 'web',
    })
  })

  it('nao manda descricao vazia', async () => {
    mockFetch(() =>
      jsonResponse(200, { photo: photo(), created: false, alsoInOtherCustomers: true }),
    )

    const result = await customerPhotoService.attach(88, 5731, '')

    expect(JSON.parse(lastBody())).toEqual({ imageId: 5731, source: 'web' })
    expect(result.created).toBe(false)
    expect(result.alsoInOtherCustomers).toBe(true)
  })

  it('descreve por PUT na rota da foto', async () => {
    mockFetch(() => jsonResponse(200, photo({ description: 'saia verde' })))

    const saved = await customerPhotoService.setDescription(88, 1204, 'saia verde')

    expect(calls.at(-1)?.init.method).toBe('PUT')
    expect(lastUrl()).toBe('/api/customers/88/photos/1204/description')
    expect(JSON.parse(lastBody())).toEqual({ description: 'saia verde' })
    expect(saved.description).toBe('saia verde')
  })

  /** FR-007 / US4-4: apagar o texto e gravar null, nao "". */
  it('manda null quando a descricao foi apagada', async () => {
    mockFetch(() => jsonResponse(200, photo({ description: null })))

    await customerPhotoService.setDescription(88, 1204, '   ')

    expect(JSON.parse(lastBody())).toEqual({ description: null })
  })

  /** R11: uma operacao por verbo — o MCP exige paridade 1:1. */
  it('favorita por PUT e desfavorita por DELETE na mesma rota', async () => {
    mockFetch(() => jsonResponse(200, photo({ isFavorite: true })))

    await customerPhotoService.favorite(88, 1204)

    expect(calls.at(-1)?.init.method).toBe('PUT')
    expect(lastUrl()).toBe('/api/customers/88/photos/1204/favorite')
    expect(lastBody()).toBe('')

    mockFetch(() => jsonResponse(200, photo({ isFavorite: false })))

    await customerPhotoService.unfavorite(88, 1204)

    expect(calls.at(-1)?.init.method).toBe('DELETE')
    expect(lastUrl()).toBe('/api/customers/88/photos/1204/favorite')
  })

  it('remove a foto na rota DELETE e aceita 204 sem corpo', async () => {
    mockFetch(() => new Response(null, { status: 204 }))

    await expect(customerPhotoService.remove(88, 1204)).resolves.toBeUndefined()

    expect(calls.at(-1)?.init.method).toBe('DELETE')
    expect(lastUrl()).toBe('/api/customers/88/photos/1204')
  })

  it('define o perfil com o recorte do navegador', async () => {
    mockFetch(() =>
      jsonResponse(200, {
        photoId: 1204,
        imageId: 5740,
        originalImageId: 5731,
        url: 'https://storage.example/5740.webp',
        originalUrl: 'https://storage.example/5731.jpg',
        urlExpiresAt: '2026-10-29T10:15:00Z',
      }),
    )

    const profile = await customerPhotoService.setProfile(88, 1204, 5740)

    expect(calls.at(-1)?.init.method).toBe('PUT')
    expect(lastUrl()).toBe('/api/customers/88/profile-photo')
    expect(JSON.parse(lastBody())).toEqual({ photoId: 1204, imageId: 5740 })
    expect(profile.originalImageId).toBe(5731)
  })

  /** R4: sem imageId o circulo usa a propria foto (caso da conversa). */
  it('define o perfil sem recorte', async () => {
    mockFetch(() =>
      jsonResponse(200, {
        photoId: 1204,
        imageId: 5731,
        originalImageId: 5731,
        url: 'https://storage.example/5731.jpg',
        originalUrl: null,
        urlExpiresAt: '2026-10-29T10:15:00Z',
      }),
    )

    await customerPhotoService.setProfile(88, 1204)

    expect(JSON.parse(lastBody())).toEqual({ photoId: 1204 })
  })

  it('remove o perfil por DELETE', async () => {
    mockFetch(() => new Response(null, { status: 204 }))

    await customerPhotoService.removeProfile(88)

    expect(calls.at(-1)?.init.method).toBe('DELETE')
    expect(lastUrl()).toBe('/api/customers/88/profile-photo')
  })

  /** FR-025: inexistente e recurso de outra vendedora chegam iguais (404). */
  it('propaga NotFoundError no 404', async () => {
    mockFetch(() =>
      jsonResponse(404, { sucesso: false, mensagem: 'Foto não encontrada.', erros: [] }),
    )

    const error = await capture(customerPhotoService.remove(88, 999))

    expect(error).toBeInstanceOf(NotFoundError)
    expect((error as NotFoundError).message).toBe('Foto não encontrada.')
  })

  /** A 400 com campo e o que a vendedora ve no editor de descricao (US4-5). */
  it('propaga ApiError com os erros de campo no 400', async () => {
    mockFetch(() =>
      jsonResponse(400, {
        sucesso: false,
        mensagem: 'Dados inválidos.',
        erros: [{ campo: 'description', mensagem: 'A descrição deve ter no máximo 500 caracteres.' }],
      }),
    )

    const error = await capture(customerPhotoService.setDescription(88, 1204, 'x'))

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).erros[0]?.campo).toBe('description')
  })

  it('propaga UnauthorizedError no 401 e chama o handler de sessao', async () => {
    const onUnauthorized = vi.fn()
    setUnauthorizedHandler(onUnauthorized)
    mockFetch(() => new Response('', { status: 401 }))

    expect(await capture(customerPhotoService.list({ customerId: 88 }))).toBeInstanceOf(
      UnauthorizedError,
    )
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })

  it('expoe somente as operacoes do contrato de fotos', () => {
    expect(Object.getOwnPropertyNames(Object.getPrototypeOf(customerPhotoService)).sort()).toEqual(
      [
        'attach',
        'constructor',
        'favorite',
        'list',
        'remove',
        'removeProfile',
        'setDescription',
        'setProfile',
        'unfavorite',
      ],
    )
  })
})
