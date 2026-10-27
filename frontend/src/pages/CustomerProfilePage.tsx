import { useCallback, useEffect, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { EmptyState } from '@/components/common/EmptyState'
import { ErrorState } from '@/components/common/ErrorState'
import { LoadingList } from '@/components/common/LoadingList'
import { NotFound } from '@/components/common/NotFound'
import { Pagination } from '@/components/common/Pagination'
import { CpfReveal } from '@/components/customers/CpfReveal'
import { CustomerAvatar } from '@/components/customers/CustomerAvatar'
import { CustomerSummary } from '@/components/customers/CustomerSummary'
import { OrderCard } from '@/components/orders/OrderCard'
import { useCustomer } from '@/hooks/useCustomer'
import { notifyError, toUserMessage } from '@/lib/errors'
import { parsePage } from '@/lib/filters'
import { NotFoundError, UnauthorizedError } from '@/Services/apiClient'
import { orderService } from '@/Services/orderService'

import type { OrderInfo } from '@/types/order'
import { PAGE_SIZE } from '@/types/common'

/**
 * Perfil do cliente (US2, FR-010/FR-011/FR-012/FR-027).
 *
 * Duas chamadas independentes: o detalhe vem do `CustomerContext` (com o CPF
 * completo, que fica só em memória) e a lista de pedidos do `orderService` com
 * `customerId` — estado LOCAL, porque é a consulta desta tela, e não do app: o
 * `OrderContext` pertence a `/pedidos` e seus filtros.
 *
 * `notFound` desenha a MESMA tela para id inexistente e para cliente de outra
 * vendedora (FR-027): nada aqui distingue os dois casos.
 */
export const CustomerProfilePage = () => {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const [searchParams, setSearchParams] = useSearchParams()

  const { selectedCustomer, loading, error, notFound, loadCustomer, retryCustomer, clearSelected } =
    useCustomer()

  const page = parsePage(searchParams)

  const [orders, setOrders] = useState<OrderInfo[]>([])
  const [ordersTotal, setOrdersTotal] = useState(0)
  const [ordersLoading, setOrdersLoading] = useState(false)
  const [ordersError, setOrdersError] = useState<string | null>(null)

  const numericId = id === undefined ? Number.NaN : Number(id)
  const validId = Number.isInteger(numericId) && numericId > 0

  useEffect(() => {
    if (!validId) {
      clearSelected()

      return
    }

    void loadCustomer(numericId)
  }, [validId, numericId, loadCustomer, clearSelected])

  /** Saiu da tela: o CPF completo vai embora com o componente (FR-022). */
  useEffect(() => {
    return () => {
      clearSelected()
    }
  }, [clearSelected])

  const loadOrders = useCallback(async (): Promise<void> => {
    if (!validId) return

    setOrdersLoading(true)
    setOrdersError(null)

    try {
      const result = await orderService.list({
        customerId: numericId,
        page,
        pageSize: PAGE_SIZE,
      })
      setOrders(result.items)
      setOrdersTotal(result.totalCount)
    } catch (err) {
      setOrders([])
      setOrdersTotal(0)

      // 404 aqui é o cliente sumindo entre as duas chamadas: a tela "não
      // encontrado" do perfil já cobre. 401 pertence ao AuthContext.
      if (err instanceof NotFoundError || err instanceof UnauthorizedError) return

      setOrdersError(toUserMessage(err, t))
      // FR-026 (T086): o toast repete a consulta desta tela.
      notifyError(err, t, () => {
        void loadOrders()
      })
    } finally {
      setOrdersLoading(false)
    }
  }, [validId, numericId, page, t])

  useEffect(() => {
    void loadOrders()
  }, [loadOrders])

  const changePage = useCallback(
    (target: number): void => {
      const next = new URLSearchParams(searchParams)
      next.set('pagina', String(target))
      setSearchParams(next)
    },
    [searchParams, setSearchParams],
  )

  const backToFirstPage = useCallback((): void => {
    const next = new URLSearchParams(searchParams)
    next.delete('pagina')
    setSearchParams(next, { replace: true })
  }, [searchParams, setSearchParams])

  const retryOrders = useCallback((): void => {
    void loadOrders()
  }, [loadOrders])

  if (!validId || notFound) {
    return (
      <NotFound
        title={t('customers.notFound')}
        linkTo="/clientes"
        linkLabel={t('customers.backToList')}
      />
    )
  }

  if (loading && selectedCustomer === null) {
    return <LoadingList count={3} />
  }

  if (error !== null && selectedCustomer === null) {
    return <ErrorState message={error} onRetry={retryCustomer} />
  }

  if (selectedCustomer === null) return null

  const ordersEmpty = !ordersLoading && orders.length === 0
  const beyondEnd = ordersEmpty && ordersTotal > 0

  return (
    <div className="flex flex-col gap-5">
      <header className="flex min-w-0 items-start gap-4">
        <CustomerAvatar
          name={selectedCustomer.name}
          photoUrl={selectedCustomer.photoUrl}
          size="lg"
        />
        <div className="min-w-0 grow">
          <h1 className="truncate text-xl font-semibold text-foreground">{selectedCustomer.name}</h1>
          <CpfReveal cpf={selectedCustomer.cpf} cpfMasked={selectedCustomer.cpfMasked} />
        </div>
      </header>

      <section aria-label={t('customers.summaryAriaLabel')} className="rounded-lg border bg-card p-4">
        <CustomerSummary summary={selectedCustomer.summary} />
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold text-foreground">{t('customers.ordersTitle')}</h2>

        {ordersLoading ? <LoadingList count={3} /> : null}

        {!ordersLoading && ordersError !== null ? (
          <ErrorState message={ordersError} onRetry={retryOrders} />
        ) : null}

        {ordersEmpty ? (
          <EmptyState
            title={t('orders.empty')}
            {...(beyondEnd
              ? { actionLabel: t('common.backToFirstPage'), onAction: backToFirstPage }
              : {})}
          />
        ) : null}

        {!ordersLoading && orders.length > 0 ? (
          <>
            <ul role="list" className="grid gap-3">
              {orders.map((order) => (
                <li key={order.id} className="min-w-0">
                  <OrderCard order={order} />
                </li>
              ))}
            </ul>
            <Pagination
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={ordersTotal}
              onPageChange={changePage}
            />
          </>
        ) : null}
      </section>
    </div>
  )
}

export default CustomerProfilePage
