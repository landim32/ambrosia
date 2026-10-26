import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { OrderKindBadge } from '@/components/orders/OrderKindBadge'
import { OrderTotal } from '@/components/orders/OrderTotal'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { formatDateOnly } from '@/lib/dates'

import type { OrderInfo } from '@/types/order'

export interface OrderCardProps {
  order: OrderInfo
}

/**
 * Card de pedido (FR-013): cliente, número, data, canal, tipo, etiqueta de
 * origem e total. Reaproveitado pelas telas que listam pedidos — `/pedidos`
 * (US3) e o perfil do cliente (US2) —, então o layout não depende de filtro.
 *
 * - `channel` sai COMO VEIO, sem tradução (FR-021, Edge Case "canal
 *   desconhecido") e com `truncate` para não quebrar os 360 px (FR-028).
 * - `origin` é etiqueta `outline` (Venda/Devolução/Cancelamento ficam no
 *   `OrderKindBadge`); origem nula não desenha etiqueta.
 * - O `OrderTotal` já traz o destaque de negativos (FR-018).
 * - O `Link` é o único alvo de toque, `min-h-11` e foco visível (FR-028/029).
 */
export const OrderCard = ({ order }: OrderCardProps) => {
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
    <Card className="border bg-card transition-colors hover:bg-accent/40">
      <Link
        to={`/pedidos/${String(order.id)}`}
        aria-label={t('orders.openOrder', { number: order.orderNumber })}
        className="flex min-h-11 flex-col gap-2 rounded-lg px-4 py-3 focus-visible:outline-none"
      >
        <div className="flex items-start justify-between gap-3">
          <span className="min-w-0 grow">
            <span className="block truncate text-sm font-medium text-foreground">
              {order.customerName}
            </span>
            <span className="block truncate font-mono text-xs text-muted-foreground">
              {order.orderNumber}
            </span>
          </span>
          <OrderTotal total={order.total} kind={order.kind} />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <OrderKindBadge kind={order.kind} />
          <span className="min-w-0 truncate text-xs text-muted-foreground tabular-nums">
            {formatDateOnly(order.orderDate)}
          </span>
          <span className="min-w-0 truncate text-xs text-muted-foreground">{channel}</span>
          {originLabel === null ? null : <Badge variant="outline">{originLabel}</Badge>}
        </div>
      </Link>
    </Card>
  )
}

export default OrderCard
