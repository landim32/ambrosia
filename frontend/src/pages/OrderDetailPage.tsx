import { useCallback, useEffect } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ChevronRight } from 'lucide-react'

import { BackLink } from '@/components/common/BackLink'
import { ErrorState } from '@/components/common/ErrorState'
import { LoadingList } from '@/components/common/LoadingList'
import { NotFound } from '@/components/common/NotFound'
import { CustomerAvatar } from '@/components/customers/CustomerAvatar'
import { OrderItemRow } from '@/components/orders/OrderItemRow'
import { OrderKindBadge } from '@/components/orders/OrderKindBadge'
import { OrderTotal } from '@/components/orders/OrderTotal'
import { Badge } from '@/components/ui/badge'
import { useOrder } from '@/hooks/useOrder'
import { formatDateOnly } from '@/lib/dates'
import { maskedCpfShape } from '@/lib/cpf'

import type { ReactNode } from 'react'
import type { OrderDetailInfo } from '@/types/order'

/** Texto ausente ou só espaços é "indisponível" (Edge Case, FR-017). */
const orDash = (value: string | null): string | null =>
  value !== null && value.trim() !== '' ? value : null

/** Par rótulo/valor do detalhe (rótulo sempre visível — FR-029). */
const Field = ({ label, children }: { label: string; children: ReactNode }) => (
  <div className="flex min-w-0 flex-col gap-0.5">
    <dt className="text-xs text-muted-foreground">{label}</dt>
    <dd className="min-w-0 break-words text-sm font-medium text-foreground tabular-nums">
      {children}
    </dd>
  </div>
)

/** Origem como ETIQUETA (FR-021); origem nula não desenha etiqueta. */
const OriginBadge = ({ origin }: { origin: OrderDetailInfo['origin'] }) => {
  const { t } = useTranslation()

  if (origin === null) return <span>{t('common.notAvailable')}</span>

  return <Badge variant="outline">{origin === 'cota' ? t('orders.originCota') : t('orders.originOmni')}</Badge>
}

export interface OrderDetailSectionsProps {
  order: OrderDetailInfo
}

/**
 * O detalhe renderizado. Separado da página para que os testes de estado
 * (carregando / erro / não encontrado) o exercitem sem mock de serviço.
 *
 * Conteúdo = FR-017, campo a campo: cliente (nome com link ao perfil + CPF
 * MASCARADO), número, data, tipo, canal, origem, status, total, "Venda original"
 * e "Cancelado em" (só quando existem) e os itens em ordem de linha.
 *
 * O que fica de fora, por escolha: `externalId`, `lineNo` impresso,
 * `createdAt`/`updatedAt` — id interno do +Vendas e auditoria não ajudam a
 * vendedora a conferir a compra (spec, seção "Itens do pedido"). `lineNo` É a
 * ordem da lista, não um número a imprimir.
 */
export const OrderDetailSections = ({ order }: OrderDetailSectionsProps) => {
  const { t } = useTranslation()
  const dash = t('common.notAvailable')

  // A ordem de linha manda na tela (FR-017). A API já devolve ordenada; ordenar
  // aqui tira esse ponto da dependência do servidor.
  const items = [...order.items].sort((a, b) => a.lineNo - b.lineNo)

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/pedidos" label={t('common.orders')} />

      {/* Cabeçalho do pedido: título grande no estilo iOS e o total em destaque. */}
      <header className="flex min-w-0 flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <OrderKindBadge kind={order.kind} />
          <span className="text-sm text-muted-foreground tabular-nums">
            {formatDateOnly(order.orderDate)}
          </span>
        </div>
        <div className="flex min-w-0 flex-wrap items-end justify-between gap-x-4 gap-y-2">
          <h1 className="min-w-0 truncate text-2xl font-bold tracking-tight text-foreground md:text-3xl">
            {t('orders.number')} <span className="font-mono">{order.orderNumber}</span>
          </h1>
          {/* FR-018: o total negativo de devolução/cancelamento já sai destacado. */}
          <OrderTotal
            total={order.total}
            kind={order.kind}
            className="text-2xl font-bold tracking-tight md:text-3xl"
          />
        </div>
      </header>

      <div className="grid gap-3 md:grid-cols-2">
        <section className="flex min-w-0 flex-col rounded-2xl border border-border/70 bg-card">
          <h2 className="px-4 pt-3 text-xs font-medium tracking-wide text-muted-foreground uppercase">
            {t('orders.customer')}
          </h2>
          <Link
            to={`/clientes/${String(order.customerId)}`}
            className="flex min-h-11 min-w-0 items-center gap-3 rounded-2xl px-4 py-3 transition-colors hover:bg-secondary/50 focus-visible:outline-none active:bg-secondary"
          >
            <CustomerAvatar
              name={order.customer.name}
              photoUrl={order.customer.profilePhoto?.url}
            />
            <span className="min-w-0 grow">
              <span className="block truncate text-[0.9375rem] font-medium text-foreground">
                {order.customer.name}
              </span>
              {/* FR-022: sempre mascarado. `order.customer` é `CustomerInfo`, que não
                  tem o campo `cpf` — não há o que revelar aqui — e `maskedCpfShape`
                  garante que nem um dado corrompido vira CPF completo na tela. */}
              <span className="block font-mono text-xs text-muted-foreground tabular-nums">
                {maskedCpfShape(order.customer.cpfMasked)}
              </span>
            </span>
            <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-muted-foreground/60" />
          </Link>
        </section>

        <section className="min-w-0 rounded-2xl border border-border/70 bg-card px-4 py-3">
          <dl className="grid grid-cols-2 gap-x-4 gap-y-3">
            <Field label={t('orders.channelLabel')}>{orDash(order.channel) ?? dash}</Field>
            <Field label={t('orders.status')}>
              <span className="truncate">{orDash(order.status) ?? dash}</span>
            </Field>
            <Field label={t('orders.origin')}>
              <OriginBadge origin={order.origin} />
            </Field>
            {order.originalSaleOn === null ? null : (
              <Field label={t('orders.originalSaleOn')}>
                {formatDateOnly(order.originalSaleOn) || dash}
              </Field>
            )}
            {order.cancelledOn === null ? null : (
              <Field label={t('orders.cancelledOn')}>
                {formatDateOnly(order.cancelledOn) || dash}
              </Field>
            )}
          </dl>
        </section>
      </div>

      <section className="flex min-w-0 flex-col gap-3">
        <h2 className="flex items-baseline gap-2 text-lg font-semibold tracking-tight text-foreground">
          {t('orders.items')}
          <span className="text-sm font-normal text-muted-foreground">
            {t('orders.itemsCount', { count: items.length })}
          </span>
        </h2>

        {items.length === 0 ? (
          <p
            className="rounded-2xl border border-dashed border-border px-4 py-8 text-center text-sm text-muted-foreground"
            data-testid="no-items"
          >
            {t('orders.noItems')}
          </p>
        ) : (
          // Fotos em destaque: 2 colunas no celular (~140 px a 320 px de tela),
          // 3 no tablet e 4 no desktop. Cada `OrderItemRow` já é o `<li>`.
          <ul role="list" className="grid min-w-0 grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {items.map((item) => (
              <OrderItemRow key={item.id} item={item} />
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

/**
 * Detalhe do pedido (US4, FR-017/FR-018/FR-022/FR-027).
 *
 * `:id` que não é inteiro positivo não vai à API: a tela de "não encontrado" é a
 * mesma do 404, para um endereço digitado à mão não parecer falha do sistema
 * (quickstart C4.4). O 404 cobre id inexistente e pedido de outra vendedora com
 * a MESMA tela (FR-027); `UnauthorizedError` fica quieto porque quem encerra a
 * sessão é o `AuthContext` (data-model §2).
 *
 * Ao sair, `clearSelected()` derruba o detalhe do contexto: quem abre o pedido
 * seguinte não vê o conteúdo do anterior enquanto o novo carrega.
 */
export const OrderDetailPage = () => {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()

  const { selectedOrder, loading, error, notFound, loadOrder, clearSelected, clearError } =
    useOrder()

  const numericId = id === undefined ? Number.NaN : Number(id)
  const validId = Number.isInteger(numericId) && numericId > 0

  useEffect(() => {
    if (!validId) {
      clearSelected()

      return
    }

    void loadOrder(numericId)
  }, [validId, numericId, loadOrder, clearSelected])

  /** Saiu da tela: o pedido do contexto vai embora com o componente. */
  useEffect(() => {
    return () => {
      clearSelected()
    }
  }, [clearSelected])

  /** FR-025/FR-026 (T086): "Tentar de novo" repete a MESMA carga da tela. */
  const retry = useCallback((): void => {
    clearError()
    if (validId) void loadOrder(numericId)
  }, [clearError, loadOrder, validId, numericId])

  if (!validId || notFound) {
    return (
      <NotFound
        title={t('orders.notFound')}
        linkTo="/pedidos"
        linkLabel={t('orders.backToOrders')}
      />
    )
  }

  if (loading && selectedOrder === null) {
    return <LoadingList count={2} />
  }

  if (error !== null && selectedOrder === null) {
    return <ErrorState message={error} onRetry={retry} />
  }

  if (selectedOrder === null) return null

  return <OrderDetailSections order={selectedOrder} />
}

export default OrderDetailPage
