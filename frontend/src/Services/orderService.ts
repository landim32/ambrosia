import { apiRequest } from '@/Services/apiClient'

import type { PagedResult } from '@/types/common'
import type { OrderDetailInfo, OrderFilters, OrderInfo } from '@/types/order'

/**
 * Order Service - consulta de pedidos da vendedora (skill `react-architecture`:
 * classe singleton).
 *
 * SOMENTE LEITURA (FR-030). A mesma rota `GET /orders` serve duas telas: a
 * lista com filtros (US3) e a lista do perfil do cliente (US2, via
 * `customerId`). Valores vazios são omitidos pelo `apiClient`/`buildQuery` e
 * `pageSize` é sempre 20 (contratos/api-consumption.md).
 */
class OrderService {
  /** `GET /orders?from&to&kind&channel&customerId&search&page&pageSize=20`. */
  async list(filters: OrderFilters): Promise<PagedResult<OrderInfo>> {
    return apiRequest<PagedResult<OrderInfo>>('GET', '/orders', {
      query: {
        from: filters.from,
        to: filters.to,
        kind: filters.kind,
        channel: filters.channel,
        customerId: filters.customerId,
        search: filters.search,
        page: filters.page,
        pageSize: filters.pageSize,
      },
    })
  }

  /**
   * `GET /orders/{id}`: detalhe com o cliente (somente `cpfMasked` — o CPF
   * completo nunca aparece no pedido, FR-022) e os itens em ordem de linha.
   * Inexistente e pedido de outra vendedora chegam iguais (404 → `NotFoundError`,
   * FR-027).
   */
  async getById(id: number): Promise<OrderDetailInfo> {
    return apiRequest<OrderDetailInfo>('GET', `/orders/${String(id)}`)
  }
}

export const orderService = new OrderService()
export default OrderService
