import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError, NotFoundError, UnauthorizedError, setUnauthorizedHandler } from '@/Services/apiClient'
import { writeSession } from '@/Services/authStorage'
import { customerService } from '@/Services/customerService'

import type { AuthSession } from '@/types/auth'
import type { PagedResult } from '@/types/common'
import type { CustomerDetailInfo, CustomerInfo, CustomerListParams } from '@/types/customer'

/**
 * customerService (T048): o que importa aqui e a MONTAGEM da chamada — path,
 * query e a propagacao dos erros do transporte. O `fetch` e o unico mock (o
 * `apiClient` ja tem suite propria, T012).
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

const CUSTOMER: CustomerInfo = {
  id: 7,
  name: 'Maria Silva',
  cpfMasked: '***.456.789-**',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-02T00:00:00Z',
}

const PAGE: PagedResult<CustomerInfo> = {
  items: [CUSTOMER],
  page: 1,
  pageSize: 20,
  totalCount: 1,
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

const params = (overrides: Partial<CustomerListParams> = {}): CustomerListParams => ({
  page: 1,
  pageSize: 20,
  ...overrides,
})

const detailOf = (overrides: Partial<CustomerDetailInfo> = {}): CustomerDetailInfo => ({
  ...CUSTOMER,
  cpf: '123.456.789-09',
  summary: {
    purchases: 3,
    netTotal: 1234.56,
    lastPurchase: '2026-03-01',
    daysSinceLastPurchase: 12,
  },
  photoCount: 0,
  ...overrides,
})

describe('customerService', () => {
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

  it('GET /customers manda search, page e pageSize=20', async () => {
    mockFetch(() => jsonResponse(200, PAGE))

    const result = await customerService.list(params({ search: 'mar', page: 2 }))

    expect(lastUrl()).toBe('/api/customers?search=mar&page=2&pageSize=20')
    expect(result.items).toEqual([CUSTOMER])
    expect(result.totalCount).toBe(1)
  })

  it('omite a busca vazia e mantem pageSize=20', async () => {
    mockFetch(() => jsonResponse(200, PAGE))

    await customerService.list(params({ search: '   ' }))

    expect(lastUrl()).toBe('/api/customers?page=1&pageSize=20')
  })

  it('envia Authorization: Bearer e Accept, sem Content-Type em GET', async () => {
    mockFetch(() => jsonResponse(200, PAGE))

    await customerService.list(params())
    const headers = lastHeaders()

    expect(headers.get('Authorization')).toBe(`Bearer ${SESSION.token}`)
    expect(headers.get('Accept')).toBe('application/json')
    expect(headers.get('Content-Type')).toBeNull()
  })

  it('getById faz GET /customers/{id} sem query', async () => {
    mockFetch(() => jsonResponse(200, detailOf()))

    const detail = await customerService.getById(7)

    expect(lastUrl()).toBe('/api/customers/7')
    expect(detail.cpf).toBe('123.456.789-09')
  })

  it('detalhe traz o resumo completo usado no perfil (FR-010)', async () => {
    mockFetch(() => jsonResponse(200, detailOf()))

    const detail = await customerService.getById(7)

    expect(detail.summary.purchases).toBe(3)
    expect(detail.summary.netTotal).toBe(1234.56)
    expect(detail.summary.lastPurchase).toBe('2026-03-01')
    expect(detail.summary.daysSinceLastPurchase).toBe(12)
  })

  /** FR-027: inexistente e cliente de outra vendedora chegam iguais (404). */
  it('getById propaga NotFoundError no 404 com ErrorResult', async () => {
    mockFetch(() =>
      jsonResponse(404, { sucesso: false, mensagem: 'Cliente não encontrado.', erros: [] }),
    )

    const error = await capture(customerService.getById(999_999))

    expect(error).toBeInstanceOf(NotFoundError)
  })

  it('getById propaga NotFoundError no 404 de corpo vazio', async () => {
    mockFetch(() => new Response('', { status: 404 }))

    expect(await capture(customerService.getById(999_999))).toBeInstanceOf(NotFoundError)
  })

  it('list propaga UnauthorizedError no 401 e chama o handler de sessão', async () => {
    const onUnauthorized = vi.fn()
    setUnauthorizedHandler(onUnauthorized)
    mockFetch(() => new Response('', { status: 401 }))

    expect(await capture(customerService.list(params()))).toBeInstanceOf(UnauthorizedError)
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })

  it('list propaga ApiError com a mensagem da API no 400', async () => {
    mockFetch(() =>
      jsonResponse(400, {
        sucesso: false,
        mensagem: 'Dados inválidos.',
        erros: [{ campo: 'page', mensagem: 'A página deve ser maior ou igual a 1.' }],
      }),
    )

    const error = await capture(customerService.list(params({ page: 0 })))

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).mensagem).toBe('Dados inválidos.')
    expect((error as ApiError).erros).toHaveLength(1)
  })

  /** FR-030: o servico expoe so leitura — nao ha criar/editar/excluir. */
  it('expoe somente os metodos de consulta', () => {
    expect(Object.getOwnPropertyNames(Object.getPrototypeOf(customerService)).sort()).toEqual(
      ['constructor', 'getById', 'list'],
    )
  })
})
