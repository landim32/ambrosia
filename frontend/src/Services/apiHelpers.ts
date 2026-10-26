import { PAGE_SIZE } from '@/types/common'

import type { AuthSession } from '@/types/auth'

/**
 * Cabeçalhos padrão das chamadas (skill `react-architecture`, regra 6).
 * `Accept` sempre; `Content-Type` só com corpo; `Authorization` só com sessão
 * quando `withAuth` é true (rotas `/auth/*` passam `withAuth: false`).
 */
export const getHeaders = (
  session: AuthSession | null,
  withBody: boolean,
  withAuth = true,
): HeadersInit => {
  const headers: Record<string, string> = {
    Accept: 'application/json',
  }

  if (withBody) headers['Content-Type'] = 'application/json'
  if (withAuth && session) headers.Authorization = `Bearer ${session.token}`

  return headers
}

/**
 * Monta a query string omitindo `undefined`, null e vazios (após `trim`).
 * `pageSize` é sempre 20 quando ausente (contratos/api-consumption.md).
 */
export const buildQuery = (
  params: Record<string, string | number | undefined | null>,
): string => {
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null) continue
    const text = typeof value === 'number' ? String(value) : value.trim()
    if (text === '') continue
    search.set(key, text)
  }

  if (!search.has('pageSize')) search.set('pageSize', String(PAGE_SIZE))

  const query = search.toString()
  return query === '' ? '' : `?${query}`
}
