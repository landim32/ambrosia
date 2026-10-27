import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  ApiError,
  apiRequest,
  NetworkError,
  NotFoundError,
  setUnauthorizedHandler,
  UnauthorizedError,
} from '@/Services/apiClient'
import { writeSession } from '@/Services/authStorage'

import type { AuthSession } from '@/types/auth'

interface Ping {
  ok: boolean
}

const SESSION: AuthSession = {
  token: 'jwt-de-teste',
  expiresAt: '2099-01-01T00:00:00Z',
  user: {
    id: 1,
    email: 'ana@exemplo.com',
    name: 'Ana',
    provider: 'password',
    createdAt: '2026-01-01T00:00:00Z',
  },
}

/** Última URL pedida e o `RequestInit` correspondente. */
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

/**
 * O corpo de um `Response` só pode ser lido uma vez: a resposta é criada DENTRO
 * do mock, nunca reutilizada entre chamadas.
 */
const jsonResponse = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })

const textResponse = (status: number, body: string): Response =>
  new Response(body, {
    status,
    headers: { 'content-type': 'text/plain' },
  })

const mockFetch = (factory: () => Response) =>
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    record(input, init)
    return Promise.resolve(factory())
  })

const lastCall = (): RecordedCall => {
  const last = calls.at(-1)
  if (!last) throw new Error('fetch não foi chamado')
  return last
}

const lastUrl = (): string => lastCall().url

const lastHeaders = (): Headers => new Headers(lastCall().init.headers)

/** Captura o erro rejeitado por `promise`. */
const capture = async (promise: Promise<unknown>): Promise<unknown> =>
  promise.then(
    () => {
      throw new Error('a requisição deveria ter falhado')
    },
    (error: unknown) => error,
  )

describe('apiClient', () => {
  beforeEach(() => {
    setUnauthorizedHandler(undefined)
    calls.length = 0
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    localStorage.clear()
  })

  it('retorna o JSON tipado em 200', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await expect(apiRequest<Ping>('GET', '/health', { auth: false })).resolves.toEqual({ ok: true })
    expect(lastUrl()).toBe('/api/health')
  })

  it('prefixa o path com VITE_API_URL', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('GET', '/customers', { auth: false })

    expect(lastUrl()).toBe('/api/customers')
  })

  it('injeta Authorization: Bearer nas rotas protegidas', async () => {
    writeSession(SESSION)
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('GET', '/customers')

    expect(lastHeaders().get('authorization')).toBe('Bearer jwt-de-teste')
  })

  it('NÃO injeta Authorization em /auth/* mesmo com sessão', async () => {
    writeSession(SESSION)
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('POST', '/auth/login', {
      body: { email: 'ana@exemplo.com', password: 'senha-forte-1' },
    })

    expect(lastHeaders().get('authorization')).toBeNull()
  })

  it('rota anônima dispensa sessão', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await expect(
      apiRequest<Ping>('POST', '/auth/google', { body: { idToken: 'x' } }),
    ).resolves.toEqual({ ok: true })
  })

  it('sempre envia Accept: application/json', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('GET', '/customers', { auth: false })

    expect(lastHeaders().get('accept')).toBe('application/json')
  })

  it('envia Content-Type: application/json em POST', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('POST', '/auth/register', {
      body: { email: 'a@a.com', name: 'A', password: 'senha-forte-1' },
    })

    expect(lastHeaders().get('content-type')).toBe('application/json')
    expect(lastCall().init.body).toBe(
      JSON.stringify({ email: 'a@a.com', name: 'A', password: 'senha-forte-1' }),
    )
    expect(lastCall().init.method).toBe('POST')
  })

  it('não envia Content-Type em GET sem corpo', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('GET', '/customers', { auth: false })

    expect(lastHeaders().get('content-type')).toBeNull()
    expect(lastCall().init.body).toBeUndefined()
  })

  it('monta a query omitindo undefined e vazios', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('GET', '/orders', {
      auth: false,
      query: {
        from: '2026-01-01',
        to: '',
        kind: undefined,
        channel: '   ',
        customerId: 7,
        page: 2,
        pageSize: 20,
      },
    })

    expect(lastUrl()).toContain('/api/orders?')
    const search = new URL(lastUrl(), 'http://localhost').searchParams
    expect(search.get('from')).toBe('2026-01-01')
    expect(search.get('customerId')).toBe('7')
    expect(search.get('page')).toBe('2')
    expect(search.get('pageSize')).toBe('20')
    expect(search.has('to')).toBe(false)
    expect(search.has('kind')).toBe(false)
    expect(search.has('channel')).toBe(false)
  })

  it('garante pageSize=20 na query de lista', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('GET', '/customers', { auth: false, query: { page: 1 } })

    const search = new URL(lastUrl(), 'http://localhost').searchParams
    expect(search.get('pageSize')).toBe('20')
  })

  it('400 com ErrorResult vira ApiError com status, mensagem e erros', async () => {
    mockFetch(() =>
      jsonResponse(400, {
        sucesso: false,
        mensagem: 'CPF inválido',
        erros: [{ campo: 'cpf', mensagem: 'dígitos verificadores não conferem' }],
      }),
    )

    const err = (await capture(apiRequest<Ping>('GET', '/customers', { auth: false }))) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(400)
    expect(err.mensagem).toBe('CPF inválido')
    expect(err.erros).toHaveLength(1)
    expect(err.erros[0]?.campo).toBe('cpf')
    expect(err.message).toBe('CPF inválido')
  })

  it('409 com ErrorResult vira ApiError(409)', async () => {
    mockFetch(() =>
      jsonResponse(409, { sucesso: false, mensagem: 'Pedido já cadastrado', erros: [] }),
    )

    const err = (await capture(apiRequest<Ping>('GET', '/orders', { auth: false }))) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(409)
  })

  it('401 em chamada autenticada vira UnauthorizedError e chama onUnauthorized', async () => {
    writeSession(SESSION)
    const onUnauthorized = vi.fn()
    setUnauthorizedHandler(onUnauthorized)
    mockFetch(() => new Response(null, { status: 401 }))

    const err = await capture(apiRequest<Ping>('GET', '/customers'))

    expect(err).toBeInstanceOf(UnauthorizedError)
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })

  // 003 · research §3 — o 401 de "credencial recusada" nasce de uma chamada ANÔNIMA
  // (`POST /auth/login`) e não pode ser confundido com sessão expirada: sem esta
  // regra a tela de entrada mostraria "Sua sessão expirou" no lugar de
  // "E-mail ou senha inválidos" e derrubaria a sessão da vendedora.
  it('401 anônimo vira ApiError(401, mensagem do ErrorResult) e NÃO chama o handler', async () => {
    const onUnauthorized = vi.fn()
    setUnauthorizedHandler(onUnauthorized)
    mockFetch(() =>
      jsonResponse(401, { sucesso: false, mensagem: 'E-mail ou senha inválidos', erros: [] }),
    )

    const err = (await capture(
      apiRequest<Ping>('POST', '/auth/login', {
        body: { email: 'ana@exemplo.com', password: 'errada-123' },
      }),
    )) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(401)
    expect(err.mensagem).toBe('E-mail ou senha inválidos')
    expect(err.erros).toEqual([])
    expect(onUnauthorized).not.toHaveBeenCalled()
  })

  it('401 anônimo preserva os erros de campo do ErrorResult', async () => {
    const onUnauthorized = vi.fn()
    setUnauthorizedHandler(onUnauthorized)
    mockFetch(() =>
      jsonResponse(401, {
        sucesso: false,
        mensagem: 'E-mail ou senha inválidos',
        erros: [{ campo: 'password', mensagem: 'A senha é obrigatória.' }],
      }),
    )

    const err = (await capture(
      apiRequest<Ping>('POST', '/auth/login', { body: { email: 'a@a.com', password: '' } }),
    )) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err.erros).toHaveLength(1)
    expect(err.erros[0]?.campo).toBe('password')
    expect(onUnauthorized).not.toHaveBeenCalled()
  })

  it('401 anônimo sem corpo JSON vira ApiError(401, texto)', async () => {
    mockFetch(() => textResponse(401, 'unauthorized'))

    const err = (await capture(
      apiRequest<Ping>('POST', '/auth/login', { body: { email: 'a@a.com', password: 'x' } }),
    )) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(401)
    expect(err.mensagem).toBe('unauthorized')
  })

  it('404 com ErrorResult vira NotFoundError com a mensagem', async () => {
    mockFetch(() =>
      jsonResponse(404, { sucesso: false, mensagem: 'Cliente não encontrado', erros: [] }),
    )

    const err = (await capture(
      apiRequest<Ping>('GET', '/customers/9', { auth: false }),
    )) as NotFoundError

    expect(err).toBeInstanceOf(NotFoundError)
    expect(err.message).toBe('Cliente não encontrado')
  })

  it('404 com corpo vazio vira NotFoundError', async () => {
    mockFetch(() => new Response(null, { status: 404 }))

    const err = await capture(
      apiRequest<Ping>('POST', '/auth/login', { body: { email: 'a@a.com', password: 'x' } }),
    )

    expect(err).toBeInstanceOf(NotFoundError)
  })

  it('500 text/plain vira ApiError(500, texto)', async () => {
    mockFetch(() => textResponse(500, 'Erro interno qualquer'))

    const err = (await capture(apiRequest<Ping>('GET', '/customers', { auth: false }))) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(500)
    expect(err.mensagem).toBe('Erro interno qualquer')
    expect(err.erros).toEqual([])
  })

  it('fetch rejeitado vira NetworkError', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))

    const err = await capture(apiRequest<Ping>('GET', '/customers', { auth: false }))

    expect(err).toBeInstanceOf(NetworkError)
  })

  it('sessão vencida não chama fetch e lança UnauthorizedError', async () => {
    writeSession({ ...SESSION, expiresAt: '2020-01-01T00:00:00Z' })
    const fetchSpy = mockFetch(() => jsonResponse(200, { ok: true }))
    const onUnauthorized = vi.fn()
    setUnauthorizedHandler(onUnauthorized)

    const err = await capture(apiRequest<Ping>('GET', '/customers'))

    expect(err).toBeInstanceOf(UnauthorizedError)
    expect(fetchSpy).not.toHaveBeenCalled()
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })

  it('sem sessão em rota protegida lança UnauthorizedError sem chamar fetch', async () => {
    const fetchSpy = mockFetch(() => jsonResponse(200, { ok: true }))

    const err = await capture(apiRequest<Ping>('GET', '/customers'))

    expect(err).toBeInstanceOf(UnauthorizedError)
    expect(fetchSpy).not.toHaveBeenCalled()
  })
})

describe('apiClient com base absoluta', () => {
  beforeEach(() => {
    calls.length = 0
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('respeita VITE_API_URL absoluta (fora do proxy do Vite)', async () => {
    vi.stubEnv('VITE_API_URL', 'http://localhost:5000')
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('GET', '/customers', { auth: false, query: { page: 1 } })

    expect(lastUrl()).toBe('http://localhost:5000/customers?page=1&pageSize=20')
  })

  it('path absoluto não ganha o prefixo da base', async () => {
    mockFetch(() => jsonResponse(200, { ok: true }))

    await apiRequest<Ping>('GET', 'http://example.test/x', { auth: false })

    expect(lastUrl()).toBe('http://example.test/x')
  })
})
