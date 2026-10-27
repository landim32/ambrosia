/**
 * Parser da URL de consulta (data-model §4, T069).
 *
 * A URL e a UNICA fonte de verdade de filtros/pagina (research §9): recarregar
 * ou compartir o endereco mantem a lista (FR-015). Ela fala portugues (`de`,
 * `ate`, `tipo`, `canal`, `busca`, `pagina`) e a API fala ingles (`from`, `to`,
 * `kind`, `channel`, `search`, `page`) — este arquivo e a unica ponte.
 *
 * Todo valor invalido morre aqui, em silencio: a tela carrega sem o filtro
 * (Edge Case "Filtro invalido no endereco" e quickstart C3.5). Descartar e
 * sempre melhor que enviar: a API responderia 400 e a vendedora veria um erro
 * em vez de uma lista.
 */
import { isValidDateOnly } from '@/lib/dates'

import type { OrderFilters, OrderInfo } from '@/types/order'
import { PAGE_SIZE } from '@/types/common'

/** Limites da API (`OrderListQueryValidator`): passar deles seria um 400. */
const CHANNEL_MAX = 60
const SEARCH_MAX = 60

/** `pagina` da URL: inteiro >= 1; qualquer outra coisa e a pagina 1. */
export const parsePage = (params: URLSearchParams): number => {
  const raw = params.get('pagina')
  if (raw === null) return 1

  // Trim: um espaco ao redor nao e motivo para perder a pagina.
  const value = Number(raw.trim())
  if (!Number.isInteger(value) || value < 1) return 1

  return value
}

/** Texto com trim; `undefined` quando vazio ou acima do limite da API. */
const boundedText = (
  params: URLSearchParams,
  key: string,
  max: number,
): string | undefined => {
  const raw = params.get(key)
  if (raw === null) return undefined

  const value = raw.trim()
  if (value === '' || value.length > max) return undefined

  return value
}

/** `tipo` da URL: um dos tres da API; o resto se ignora (nunca se "conserta"). */
const parseKind = (params: URLSearchParams): OrderInfo['kind'] | undefined => {
  const raw = params.get('tipo')
  if (raw === 'sale' || raw === 'return' || raw === 'cancellation') return raw

  return undefined
}

/** Data de periodo: `AAAA-MM-DD` real, ou nada. */
const parseDate = (params: URLSearchParams, key: string): string | undefined => {
  const raw = params.get(key)
  if (raw === null) return undefined

  const value = raw.trim()

  return isValidDateOnly(value) ? value : undefined
}

const toNumber = (params: URLSearchParams, key: string): number | undefined => {
  const raw = params.get(key)
  if (raw === null) return undefined

  const value = Number(raw)

  return Number.isInteger(value) && value > 0 ? value : undefined
}

/** URL (`?de=&ate=&tipo=&canal=&busca=&pagina=`) → `OrderFilters` da API. */
export const parseOrderFilters = (params: URLSearchParams): OrderFilters => {
  const from = parseDate(params, 'de')
  const to = parseDate(params, 'ate')

  // Intervalo invertido nao tem leitura possivel: ignora os dois, a lista vem
  // sem filtro de data em vez de vazia ou com 400 da API (Edge Case).
  const inverted = from !== undefined && to !== undefined && from > to

  const customerId = toNumber(params, 'cliente')
  const kind = parseKind(params)
  const channel = boundedText(params, 'canal', CHANNEL_MAX)
  const search = boundedText(params, 'busca', SEARCH_MAX)

  return {
    ...(from !== undefined && !inverted ? { from } : {}),
    ...(to !== undefined && !inverted ? { to } : {}),
    ...(kind !== undefined ? { kind } : {}),
    ...(channel !== undefined ? { channel } : {}),
    ...(customerId !== undefined ? { customerId } : {}),
    ...(search !== undefined ? { search } : {}),
    page: parsePage(params),
    pageSize: PAGE_SIZE,
  }
}

/**
 * `OrderFilters` → URL. O caminho inverso de `parseOrderFilters`, omitindo os
 * vazios e a pagina 1 para o endereco ficar limpo (`/pedidos` em vez de
 * `/pedidos?pagina=1`).
 */
export const toSearchParams = (filters: OrderFilters): URLSearchParams => {
  const params = new URLSearchParams()

  if (filters.from !== undefined) params.set('de', filters.from)
  if (filters.to !== undefined) params.set('ate', filters.to)
  if (filters.kind !== undefined) params.set('tipo', filters.kind)
  if (filters.channel !== undefined && filters.channel.trim() !== '') {
    params.set('canal', filters.channel.trim())
  }
  if (filters.search !== undefined && filters.search.trim() !== '') {
    params.set('busca', filters.search.trim())
  }
  if (filters.customerId !== undefined) params.set('cliente', String(filters.customerId))
  if (filters.page > 1) params.set('pagina', String(filters.page))

  return params
}
