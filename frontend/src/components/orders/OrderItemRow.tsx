import { useTranslation } from 'react-i18next'

import { LazyImage } from '@/components/common/LazyImage'
import { formatCurrency } from '@/lib/currency'

import type { OrderItemInfo } from '@/types/order'

export interface OrderItemRowProps {
  item: OrderItemInfo
}

/** Um atributo do item: rótulo discreto + valor (ou o traço de indisponível). */
const Attribute = ({ label, value }: { label: string; value: string }) => (
  <span className="inline-flex min-w-0 items-baseline gap-1">
    <span className="text-xs text-muted-foreground">{label}</span>
    <span className="truncate text-xs text-muted-foreground" data-testid={`item-${label}`}>
      {value}
    </span>
  </span>
)

/**
 * Linha do item do pedido (FR-017, T079).
 *
 * A ordem de linha vem da própria lista: `OrderDetailPage` ordena por `lineNo`
 * antes de desenhar, então aqui não se imprime o número — ele É a ordem.
 *
 * Valor `null` da API (produto sem nome, sem tamanho, sem preço) sai como o traço
 * de "indisponível" (`common.notAvailable`): nem a palavra "null" na tela, nem
 * buraco que desalinhe a linha. Nome e `alt` da imagem caem no SKU quando o nome
 * falta — a API aceita o item com um dos dois.
 */
export const OrderItemRow = ({ item }: OrderItemRowProps) => {
  const { t } = useTranslation()
  const dash = t('common.notAvailable')

  const label = item.productName ?? item.skuRef ?? t('orders.noImage')

  return (
    <li className="flex items-center gap-3 py-2" data-testid="order-item-row">
      <LazyImage src={item.imageUrl} alt={label} />

      <div className="grid min-w-0 grow gap-0.5">
        <p className="truncate text-sm font-medium text-foreground">{label}</p>
        <div className="flex flex-wrap items-center gap-x-3 gap-y-0.5">
          <Attribute label={t('orders.reference')} value={item.skuRef ?? dash} />
          <Attribute label={t('orders.size')} value={item.size ?? dash} />
        </div>
      </div>

      <span className="shrink-0 text-sm font-medium text-foreground tabular-nums">
        {item.price === null ? dash : formatCurrency(item.price)}
      </span>
    </li>
  )
}

export default OrderItemRow
