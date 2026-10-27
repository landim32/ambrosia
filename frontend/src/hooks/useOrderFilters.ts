import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'

import { parseOrderFilters, toSearchParams } from '@/lib/filters'

import type { OrderFilters } from '@/types/order'

export interface UseOrderFiltersResult {
  /** Filtros já validados, lidos da URL (a única fonte de verdade). */
  filters: OrderFilters
  /** Mudar filtro: repõe `page: 1` e usa `replace` (contratos/ui-routes.md). */
  setFilters: (patch: Partial<OrderFilters>) => void
  /** Mudar página: `push`, para o botão voltar percorrer páginas. */
  setPage: (page: number) => void
  /** "Limpar filtros": só a página 1, sem filtro nenhum. */
  clearFilters: () => void
}

/**
 * Filtros de `/pedidos` na URL (research §9, T070).
 *
 * `useSearchParams` é o estado — nada de espelho no Context, que teria duas
 * fontes de verdade divergindo. Na escrita, `toSearchParams` só conhece os
 * parâmetros da tela, então um valor inválido colado na barra (`tipo=invalido`,
 * `de=2026-13-99`) morre no `parseOrderFilters` e NÃO volta para a URL na
 * próxima navegação (Edge Case "Filtro inválido no endereço").
 *
 * `cliente` (perfil, US2) não é filtro desta tela: é preservado na escrita.
 */
export const useOrderFilters = (): UseOrderFiltersResult => {
  const [searchParams, setSearchParams] = useSearchParams()

  const filters = useMemo<OrderFilters>(() => parseOrderFilters(searchParams), [searchParams])

  const write = useCallback(
    (next: OrderFilters, replace: boolean): void => {
      const params = toSearchParams(next)

      const customerId = searchParams.get('cliente')
      if (customerId !== null && !params.has('cliente')) params.set('cliente', customerId)

      setSearchParams(params, { replace })
    },
    [searchParams, setSearchParams],
  )

  const setFilters = useCallback(
    (patch: Partial<OrderFilters>): void => {
      // Filtrar é uma escolha: volta à página 1 e não entope o histórico.
      write({ ...filters, ...patch, page: 1 }, true)
    },
    [filters, write],
  )

  const setPage = useCallback(
    (page: number): void => {
      write({ ...filters, page }, false)
    },
    [filters, write],
  )

  const clearFilters = useCallback((): void => {
    write({ page: 1, pageSize: filters.pageSize }, true)
  }, [filters.pageSize, write])

  return { filters, setFilters, setPage, clearFilters }
}

export default useOrderFilters
