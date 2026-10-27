import { useTranslation } from 'react-i18next'

import { LazyImage } from '@/components/common/LazyImage'
import { useStoredImageUrl } from '@/hooks/useStoredImageUrl'
import { formatCurrency } from '@/lib/currency'

import type { OrderItemInfo } from '@/types/order'

export interface OrderItemRowProps {
  item: OrderItemInfo
}

/** Um atributo do item: rótulo discreto + valor (ou o traço de indisponível). */
const Attribute = ({ label, value }: { label: string; value: string }) => (
  <span className="inline-flex min-w-0 items-baseline gap-1">
    <span className="shrink-0 text-xs text-muted-foreground/80">{label}</span>
    <span className="truncate text-xs text-foreground/90" data-testid={`item-${label}`}>
      {value}
    </span>
  </span>
)

/**
 * Item do pedido (FR-017, T079) como CARTÃO com a foto em destaque — a peça de
 * roupa é o que a vendedora reconhece primeiro. A foto ocupa a largura do
 * cartão (quadrada), e a lista (`OrderDetailSections`) põe 2 colunas no
 * celular e até 4 no desktop.
 *
 * A ordem de linha vem da própria lista: `OrderDetailPage` ordena por `lineNo`
 * antes de desenhar, então aqui não se imprime o número — ele É a ordem.
 *
 * Valor `null` da API (produto sem nome, sem tamanho, sem preço) sai como o traço
 * de "indisponível" (`common.notAvailable`): nem a palavra "null" na tela, nem
 * buraco que desalinhe o cartão. Nome e `alt` da imagem caem no SKU quando o nome
 * falta — a API aceita o item com um dos dois.
 */
export const OrderItemRow = ({ item }: OrderItemRowProps) => {
  const { t } = useTranslation()
  const dash = t('common.notAvailable')

  const label = item.productName ?? item.skuRef ?? t('orders.noImage')

  // Foto importada (feature 006): vem do storage pela URL pré-assinada do
  // `imageId`. A `imageUrl` desses itens é a da origem, que só abre com a sessão
  // do +Vendas, então não serve de reserva; ela só é usada sem `imageId`.
  const storedUrl = useStoredImageUrl(item.imageId)
  const src = item.imageId === null ? item.imageUrl : (storedUrl ?? null)

  return (
    <li
      className="flex min-w-0 flex-col overflow-hidden rounded-2xl border border-border/70 bg-card"
      data-testid="order-item-row"
    >
      <LazyImage src={src} alt={label} className="w-full rounded-none ring-0" />

      <div className="flex min-w-0 grow flex-col gap-1.5 p-3">
        <p className="line-clamp-2 text-sm leading-snug font-medium break-words text-foreground">
          {label}
        </p>
        <div className="flex min-w-0 flex-col gap-0.5">
          <Attribute label={t('orders.reference')} value={item.skuRef ?? dash} />
          <Attribute label={t('orders.size')} value={item.size ?? dash} />
        </div>
        <span className="mt-auto pt-1 text-[0.9375rem] font-semibold text-foreground tabular-nums">
          {item.price === null ? dash : formatCurrency(item.price)}
        </span>
      </div>
    </li>
  )
}

export default OrderItemRow
