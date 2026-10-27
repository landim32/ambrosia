import { useCallback, useEffect, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ReceiptText } from 'lucide-react'

import { BackLink } from '@/components/common/BackLink'
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
import { formatDateTime } from '@/lib/dates'
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

  const since = formatDateTime(selectedCustomer.createdAt).slice(0, 10)

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/clientes" label={t('common.customers')} />

      {/* Cabeçalho de perfil (estilo rede social): avatar grande com anel no
          gradiente da marca, nome, CPF e "Cliente desde". */}
      <header className="relative isolate flex min-w-0 flex-col items-center gap-3 text-center">
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-x-0 -top-10 -z-10 mx-auto h-48 max-w-md rounded-full bg-brand/25 blur-3xl"
        />
        <div className="rounded-full bg-gradient-to-tr from-brand via-brand-soft to-brand-cyan p-[3px]">
          <div className="rounded-full bg-background p-[3px]">
            <CustomerAvatar
              name={selectedCustomer.name}
              photoUrl={selectedCustomer.photoUrl}
              size="xl"
            />
          </div>
        </div>
        <div className="flex w-full min-w-0 flex-col items-center gap-1">
          <h1 className="w-full truncate text-2xl font-bold tracking-tight text-foreground md:text-3xl">
            {selectedCustomer.name}
          </h1>
          <div className="flex justify-center">
            <CpfReveal cpf={selectedCustomer.cpf} cpfMasked={selectedCustomer.cpfMasked} />
          </div>
          {since === '' ? null : (
            <p className="text-xs text-muted-foreground tabular-nums">
              {t('customers.since', { date: since })}
            </p>
          )}
        </div>
      </header>

      <section aria-label={t('customers.summaryAriaLabel')} className="mx-auto w-full max-w-2xl">
        <CustomerSummary summary={selectedCustomer.summary} />
      </section>

      <section className="flex flex-col gap-3">
        {/* Aba única de "publicações" do perfil: os pedidos do cliente. */}
        <div className="flex items-center gap-2 border-b border-border/70">
          <h2 className="-mb-px inline-flex min-h-11 items-center gap-2 border-b-2 border-brand px-1 text-sm font-semibold text-foreground">
            <ReceiptText aria-hidden="true" className="size-4 text-brand-soft" />
            {t('customers.ordersTitle')}
            {ordersTotal > 0 ? (
              <span className="rounded-full bg-secondary px-2 py-0.5 text-xs font-medium text-muted-foreground tabular-nums">
                {ordersTotal}
              </span>
            ) : null}
          </h2>
        </div>

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
            <ul role="list" className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {orders.map((order) => (
                <li key={order.id} className="min-w-0">
                  <OrderCard order={order} hideCustomer />
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
