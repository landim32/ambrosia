import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import { orderKindLabel } from '@/lib/orderKind'

import type { OrderInfo } from '@/types/order'

export interface OrderKindBadgeProps {
  kind: OrderInfo['kind']
}

/**
 * Etiqueta do tipo do pedido em português (FR-021): Venda / Devolução /
 * Cancelamento.
 *
 * Devolução e cancelamento usam a variante `destructive` — o destaque visual que
 * a FR-018 pede para diferenciá-los da venda, em lista e no detalhe, e o par do
 * total negativo do `OrderTotal`.
 */
export const OrderKindBadge = ({ kind }: OrderKindBadgeProps) => {
  const { t } = useTranslation()

  return (
    <Badge
      variant={kind === 'sale' ? 'secondary' : 'destructive'}
      data-testid={`order-kind-${kind}`}
    >
      {t(orderKindLabel(kind))}
    </Badge>
  )
}

export default OrderKindBadge
