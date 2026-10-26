import type { AuthSession } from '@/types/auth'

/** Chave da constituição §VI — nunca cookies, nunca outro nome. */
export const AUTH_STORAGE_KEY = 'login-with-metamask:auth'

/** Resultado de uma leitura que NÃO apaga nada (ver `peekSession`). */
export interface SessionPeek {
  session: AuthSession | null
  /** `true` quando a chave existe, mesmo com conteúdo inválido ou vencido. */
  keyWasPresent: boolean
}

const isSession = (value: unknown): value is AuthSession => {
  if (value === null || typeof value !== 'object') return false

  const candidate = value as Record<string, unknown>
  const { token, expiresAt, user } = candidate

  if (typeof token !== 'string' || token.trim() === '') return false
  if (typeof expiresAt !== 'string') return false

  const when = Date.parse(expiresAt)
  if (Number.isNaN(when) || when <= Date.now()) return false

  if (user === null || typeof user !== 'object') return false
  const profile = user as Record<string, unknown>

  return (
    typeof profile.id === 'number' &&
    typeof profile.email === 'string' &&
    profile.email !== '' &&
    typeof profile.name === 'string' &&
    (profile.provider === 'google' || profile.provider === 'dev') &&
    typeof profile.createdAt === 'string'
  )
}

/**
 * Lê a sessão do `localStorage` aplicando as validações do data-model §3.
 * Qualquer JSON inválido, token vazio, `expiresAt` não parseável ou no passado
 * descarta a chave por completo (a vendedora volta a ser anônima).
 */
export const readSession = (): AuthSession | null => {
  const peek = peekSession()
  if (peek.session) return peek.session
  if (peek.keyWasPresent) localStorage.removeItem(AUTH_STORAGE_KEY)

  return null
}

/**
 * Leitura não destrutiva: distingue "nunca houve sessão" (vendedora anônima, sem
 * aviso) de "a chave existe mas está corrompida ou vencida" (sessão expirada,
 * cenários 5 e 6 do quickstart). Não remove nada.
 */
export const peekSession = (): SessionPeek => {
  const raw = localStorage.getItem(AUTH_STORAGE_KEY)
  if (raw === null) return { session: null, keyWasPresent: false }

  try {
    const parsed: unknown = JSON.parse(raw)
    if (isSession(parsed)) return { session: parsed, keyWasPresent: true }
  } catch {
    /* JSON corrompido: cai no retorno abaixo. */
  }

  return { session: null, keyWasPresent: true }
}

/** Grava a sessão obtida no login. Nada além disso é persistido. */
export const writeSession = (session: AuthSession): void => {
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session))
}

/** Remove a chave (usado no "Sair" e na expiração). */
export const clearSession = (): void => {
  localStorage.removeItem(AUTH_STORAGE_KEY)
}
