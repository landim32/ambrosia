import { useCallback, useEffect } from 'react'
import { useTranslation } from 'react-i18next'

import { EmptyState } from '@/components/common/EmptyState'
import { ErrorState } from '@/components/common/ErrorState'
import { LoadingList } from '@/components/common/LoadingList'
import { OrderFiltersBar } from '@/components/orders/OrderFiltersBar'
import { Pagination } from '@/components/common/Pagination'
import { OrderCard } from '@/components/orders/OrderCard'
import { useOrder } from '@/hooks/useOrder'
import { useOrderFilters } from '@/hooks/useOrderFilters'
import { PAGE_SIZE } from '@/types/common'

/**
 * Lista de pedidos com filtros (US3, FR-013/FR-014/FR-015/FR-018).
 *
 * A URL é o estado (research §9): `useOrderFilters` lê os filtros dela e cada
 * mudança volta para a URL, então recarregar ou compartilhar o endereço mostra a
 * mesma lista (FR-015). Parâmetro inválido já morreu no parser, e a tela carrega
 * sem ele (quickstart C3.5) em vez de mostrar um erro.
 *
 * Sem filtro (abertura ou "Limpar filtros") a lista traz TODOS os pedidos, sem
 * data (FR-014). Página além do fim: `items: []` com `totalCount` > 0 → "Voltar à
 * primeira página" (Edge Case).
 */
export const OrdersPage = () => {
  const { t } = useTranslation()
  const { filters, setFilters, setPage, clearFilters } = useOrderFilters()
  const { orders, totalCount, loading, error, loadOrders, retryOrders } = useOrder()

  useEffect(() => {
    void loadOrders(filters)
  }, [loadOrders, filters])

  const clear = useCallback((): void => {
    clearFilters()
  }, [clearFilters])

  const firstPage = useCallback((): void => {
    setPage(1)
  }, [setPage])

  const isEmpty = !loading && error === null && orders.length === 0
  const beyondEnd = isEmpty && totalCount > 0

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-3xl font-bold tracking-tight text-foreground">{t('orders.title')}</h1>

      <OrderFiltersBar filters={filters} onChange={setFilters} onClear={clear} />

      {loading ? <LoadingList count={4} /> : null}

      {/* FR-025/FR-026 (T086): o contexto guardou os filtros da última consulta
          — "Tentar de novo" repete a mesma busca, sem tocar na URL. */}
      {!loading && error !== null ? <ErrorState message={error} onRetry={retryOrders} /> : null}

      {isEmpty ? (
        <EmptyState
          title={t('orders.empty')}
          actionLabel={beyondEnd ? t('common.backToFirstPage') : t('orders.clearFilters')}
          onAction={beyondEnd ? firstPage : clear}
        />
      ) : null}

      {!loading && orders.length > 0 ? (
        <>
          <ul role="list" className="grid gap-3 lg:grid-cols-2">
            {orders.map((order) => (
              <li key={order.id} className="min-w-0">
                <OrderCard order={order} />
              </li>
            ))}
          </ul>

          <Pagination
            page={filters.page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={setPage}
          />
        </>
      ) : null}
    </div>
  )
}

export default OrdersPage
