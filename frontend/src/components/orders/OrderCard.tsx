import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ChevronRight } from 'lucide-react'

import { OrderKindBadge } from '@/components/orders/OrderKindBadge'
import { OrderTotal } from '@/components/orders/OrderTotal'
import { Badge } from '@/components/ui/badge'
import { formatDateOnly } from '@/lib/dates'

import type { OrderInfo } from '@/types/order'

export interface OrderCardProps {
  order: OrderInfo
  /**
   * No perfil do cliente o nome dele já está no topo da tela: o card abre com
   * o número do pedido em vez de repetir o nome.
   */
  hideCustomer?: boolean
}

/**
 * Card de pedido (FR-013): cliente, número, data, canal, tipo, etiqueta de
 * origem e total. Reaproveitado pelas telas que listam pedidos — `/pedidos`
 * (US3) e o perfil do cliente (US2) —, então o layout não depende de filtro.
 *
 * - `channel` sai COMO VEIO, sem tradução (FR-021, Edge Case "canal
 *   desconhecido") e com `truncate` para não quebrar os 320 px (FR-028).
 * - `origin` é etiqueta `outline` (Venda/Devolução/Cancelamento ficam no
 *   `OrderKindBadge`); origem nula não desenha etiqueta.
 * - O `OrderTotal` já traz o destaque de negativos (FR-018).
 * - O `Link` é o único alvo de toque, `min-h-11` e foco visível (FR-028/029).
 */
export const OrderCard = ({ order, hideCustomer = false }: OrderCardProps) => {
  const { t } = useTranslation()

  const channel =
    order.channel !== null && order.channel.trim() !== ''
      ? order.channel
      : t('common.notAvailable')
  const originLabel =
    order.origin === 'cota'
      ? t('orders.originCota')
      : order.origin === 'omni'
        ? t('orders.originOmni')
        : null

  return (
    <Link
      to={`/pedidos/${String(order.id)}`}
      aria-label={t('orders.openOrder', { number: order.orderNumber })}
      className="group flex min-h-11 flex-col gap-3 rounded-2xl border border-border/70 bg-card px-4 py-3.5 transition-colors hover:border-brand/40 hover:bg-secondary/50 focus-visible:outline-none active:bg-secondary"
    >
      <div className="flex items-start justify-between gap-3">
        <span className="min-w-0 grow">
          {hideCustomer ? (
            <span className="block truncate font-mono text-[0.9375rem] font-medium text-foreground">
              {order.orderNumber}
            </span>
          ) : (
            <>
              <span className="block truncate text-[0.9375rem] font-medium text-foreground">
                {order.customerName}
              </span>
              <span className="block truncate font-mono text-xs text-muted-foreground">
                {order.orderNumber}
              </span>
            </>
          )}
        </span>
        <span className="flex shrink-0 items-center gap-1">
          <OrderTotal total={order.total} kind={order.kind} className="text-[0.9375rem]" />
          <ChevronRight
            aria-hidden="true"
            className="size-4 text-muted-foreground/60 transition-transform group-hover:translate-x-0.5"
          />
        </span>
      </div>

      <div className="flex flex-wrap items-center gap-x-2 gap-y-1.5">
        <OrderKindBadge kind={order.kind} />
        {originLabel === null ? null : <Badge variant="outline">{originLabel}</Badge>}
        <span className="min-w-0 truncate text-xs text-muted-foreground tabular-nums">
          {formatDateOnly(order.orderDate)}
        </span>
        <span aria-hidden="true" className="text-xs text-muted-foreground/50">
          ·
        </span>
        <span className="min-w-0 truncate text-xs text-muted-foreground">{channel}</span>
      </div>
    </Link>
  )
}

export default OrderCard
