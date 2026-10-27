import { apiRequest } from '@/Services/apiClient'

import type { PagedResult } from '@/types/common'
import type { CustomerDetailInfo, CustomerInfo, CustomerListParams } from '@/types/customer'

/**
 * Customer Service - consulta de clientes da vendedora (skill
 * `react-architecture`: classe singleton, metodos finos sobre o `apiClient`).
 *
 * SOMENTE LEITURA (FR-030): nao existem criar/editar/excluir aqui. O `pageSize`
 * e sempre 20 (contratos/api-consumption.md) e os erros chegam como classes do
 * transporte — o servico NAO checa `result.sucesso` em 200, que e a excecao
 * formal registrada no Complexity Tracking do plano.
 */
class CustomerService {
  /** `GET /customers?search&page&pageSize=20` (FR-008/FR-009). */
  async list(params: CustomerListParams): Promise<PagedResult<CustomerInfo>> {
    return apiRequest<PagedResult<CustomerInfo>>('GET', '/customers', {
      query: {
        search: params.search,
        page: params.page,
        pageSize: params.pageSize,
      },
    })
  }

  /**
   * `GET /customers/{id}`: traz o CPF COMPLETO e o resumo de compras. O CPF
   * fica so em memoria no `CustomerContext` (nunca em storage/log, FR-022).
   * 404 vira `NotFoundError` — inexistente e cliente de outra vendedora sao
   * indistingativeis por design (FR-027).
   */
  async getById(id: number): Promise<CustomerDetailInfo> {
    return apiRequest<CustomerDetailInfo>('GET', `/customers/${String(id)}`)
  }
}

export const customerService = new CustomerService()
export default CustomerService
