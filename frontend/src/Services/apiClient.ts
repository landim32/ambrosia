import { buildQuery, getHeaders } from '@/Services/apiHelpers'
import { readSession } from '@/Services/authStorage'
import { PAGE_SIZE } from '@/types/common'

import type { ErrorResult, FieldError } from '@/types/common'

/** Base da API: relativa à origem (`/api`) por padrão. Lida na chamada. */
const apiBase = (): string => {
  const configured = import.meta.env.VITE_API_URL as string | undefined
  return configured && configured.trim() !== '' ? configured : '/api'
}

/** Erro de negócio da API (400/409/500) com a mensagem e os erros de campo. */
export class ApiError extends Error {
  readonly status: number
  readonly mensagem: string
  readonly erros: FieldError[]

  constructor(status: number, mensagem: string, erros: FieldError[] = []) {
    super(mensagem)
    this.name = 'ApiError'
    this.status = status
    this.mensagem = mensagem
    this.erros = erros
  }
}

/** 401, ou sessão local vencida antes do envio. */
export class UnauthorizedError extends Error {
  constructor() {
    super('Sessão inválida ou expirada')
    this.name = 'UnauthorizedError'
  }
}

/** 404: inexistente ou recurso de outra vendedora (indistinguível por design). */
export class NotFoundError extends Error {
  constructor(mensagem?: string) {
    super(mensagem ?? 'Recurso não encontrado')
    this.name = 'NotFoundError'
  }
}

/** Falha de transporte (offline, DNS, proxy). */
export class NetworkError extends Error {
  constructor() {
    super('Não foi possível conectar')
    this.name = 'NetworkError'
  }
}

type UnauthorizedHandler = (() => void) | undefined

let onUnauthorized: UnauthorizedHandler

/** Registro do callback de 401 feito pelo `AuthContext` (research §5/§6). */
export const setUnauthorizedHandler = (handler: UnauthorizedHandler): void => {
  onUnauthorized = handler
}

const triggerUnauthorized = (): void => {
  onUnauthorized?.()
}

const isJson = (response: Response): boolean =>
  (response.headers.get('content-type') ?? '').includes('application/json')

const readErrorResult = async (response: Response): Promise<ErrorResult | null> => {
  if (!isJson(response)) return null

  try {
    const body: unknown = await response.json()
    if (body === null || typeof body !== 'object' || !('mensagem' in body)) return null

    const candidate = body as Partial<ErrorResult>
    return {
      sucesso: false,
      mensagem: typeof candidate.mensagem === 'string' ? candidate.mensagem : '',
      erros: Array.isArray(candidate.erros) ? candidate.erros : [],
    }
  } catch {
    return null
  }
}

const readText = async (response: Response): Promise<string> => {
  try {
    return await response.text()
  } catch {
    return ''
  }
}

/** Sessão utilizável: existe e ainda não expirou (verificação pré-envio, FR-004). */
const liveSession = () => {
  const session = readSession()
  if (!session) return null

  const expiresAt = Date.parse(session.expiresAt)
  if (Number.isNaN(expiresAt) || expiresAt <= Date.now()) return null

  return session
}

export interface RequestOptions {
  /** Parâmetros de query; vazios são omitidos e `pageSize` sempre vale 20. */
  query?: Record<string, string | number | undefined | null>
  /** Corpo JSON enviado em POST. */
  body?: unknown
  /** `false` força chamada anônima. Padrão: `/auth/*` é anônima, o resto não. */
  auth?: boolean
}

/**
 * Cliente HTTP único da aplicação (Fetch API nativa, constituição §II).
 * Interpreta as respostas conforme `contracts/api-consumption.md`:
 * 200 → JSON tipado · 400/409 → ApiError · 401 → UnauthorizedError ·
 * 404 → NotFoundError · 500 `text/plain` → ApiError · rede → NetworkError.
 *
 * Exceção formal à regra 7 da skill `react-architecture`: respostas 200 da API da
 * HU 001 são DTOs puros, sem wrapper `sucesso` (plano → Complexity Tracking); os
 * erros chegam como classes e os contextos reagem por tipo.
 */
export const apiRequest = async <T>(
  method: 'GET' | 'POST' | 'PUT' | 'DELETE',
  path: string,
  options: RequestOptions = {},
): Promise<T> => {
  const withAuth = options.auth ?? !path.startsWith('/auth/')
  const session = withAuth ? liveSession() : null

  if (withAuth && !session) {
    triggerUnauthorized()
    throw new UnauthorizedError()
  }

  const isAbsolute = /^https?:\/\//.test(path)
  const query = options.query ? buildQuery({ ...options.query, pageSize: PAGE_SIZE }) : ''

  let response: Response
  try {
    response = await fetch(`${isAbsolute ? '' : apiBase()}${path}${query}`, {
      method,
      headers: getHeaders(session, options.body !== undefined, withAuth),
      ...(options.body === undefined ? {} : { body: JSON.stringify(options.body) }),
    })
  } catch {
    throw new NetworkError()
  }

  if (response.ok) {
    if (response.status === 204) return undefined as T
    return (await response.json()) as T
  }

  if (response.status === 401) {
    triggerUnauthorized()
    throw new UnauthorizedError()
  }

  const errorResult = await readErrorResult(response)

  if (response.status === 404) throw new NotFoundError(errorResult?.mensagem)

  if (errorResult) throw new ApiError(response.status, errorResult.mensagem, errorResult.erros)

  throw new ApiError(response.status, await readText(response))
}
