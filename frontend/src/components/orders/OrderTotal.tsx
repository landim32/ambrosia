import { formatCurrency } from '@/lib/currency'
import { isNegativeKind } from '@/lib/orderKind'
import { cn } from '@/lib/utils'

import type { OrderInfo } from '@/types/order'

export interface OrderTotalProps {
  total: number
  kind: OrderInfo['kind']
  className?: string
}

/**
 * Total do pedido em reais (FR-019) com o destaque de negativos (FR-018):
 * devolução e cancelamento saem em `text-destructive font-semibold`. O sinal não
 * é inventado aqui — a API já devolve `total` negativo nesses tipos, e o
 * `formatCurrency` do `Intl` imprime `-R$ 1.234,56`.
 */
export const OrderTotal = ({ total, kind, className }: OrderTotalProps) => (
  <span
    className={cn(
      'whitespace-nowrap text-sm tabular-nums',
      isNegativeKind(kind) ? 'font-semibold text-destructive' : 'font-medium text-foreground',
      className,
    )}
    data-testid="order-total"
  >
    {formatCurrency(total)}
  </span>
)

export default OrderTotal
