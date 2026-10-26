import type { CustomerInfo } from '@/types/customer'

/** Order Types — pedido e itens (data-model §1, openapi.yaml da HU 001) */

/** Pedido em listagens. `orderNumber` é texto (a API aceita `00004751`). */
export interface OrderInfo {
  id: number
  customerId: number
  customerName: string
  orderNumber: string
  /** Dia-calendário `AAAA-MM-DD` (FR-020). */
  orderDate: string
  kind: 'sale' | 'return' | 'cancellation'
  channel: string | null
  origin: 'cota' | 'omni' | null
  status: string | null
  /** Negativo em devolução/cancelamento (FR-018). */
  total: number
  itemsCount: number
  createdAt: string
  updatedAt: string
}

/** Linha do pedido no detalhe. */
export interface OrderItemInfo {
  id: number
  lineNo: number
  productName: string | null
  skuRef: string | null
  size: string | null
  price: number | null
  imageUrl: string | null
}

/** Todos os tipos de pedido aceitos pela API. */
export const ORDER_KINDS: readonly OrderInfo['kind'][] = ['sale', 'return', 'cancellation']

/** Detalhe do pedido com cliente e itens. */
export interface OrderDetailInfo extends OrderInfo {
  originalSaleOn: string | null
  cancelledOn: string | null
  externalId: string | null
  /** Sempre `cpfMasked` — o CPF completo nunca aparece no pedido (FR-022). */
  customer: CustomerInfo
  items: OrderItemInfo[]
}

/** Parâmetros de `GET /orders`. */
export interface OrderFilters {
  from?: string
  to?: string
  kind?: OrderInfo['kind']
  channel?: string
  customerId?: number
  search?: string
  page: number
  pageSize: 20
}
