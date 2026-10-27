/** Customer Types — cliente da vendedora (data-model §1) */

/** Cliente em listagens: CPF sempre mascarado. */
export interface CustomerInfo {
  id: number
  name: string
  /** Formato da API, ex.: `***.456.789-**`. */
  cpfMasked: string
  createdAt: string
  updatedAt: string
  /** Reservado para a HU 010; a API da 001 ainda não devolve. */
  photoUrl?: string | null
}

/** Resumo de compras exibido no perfil. */
export interface CustomerSummaryInfo {
  purchases: number
  netTotal: number
  /** Dia-calendário `AAAA-MM-DD`, ou null sem compras. */
  lastPurchase: string | null
  daysSinceLastPurchase: number | null
}

/** Detalhe do cliente: inclui o CPF completo, revelado sob demanda. */
export interface CustomerDetailInfo extends CustomerInfo {
  cpf: string
  summary: CustomerSummaryInfo
}

/** Parâmetros de `GET /customers`. */
export interface CustomerListParams {
  search?: string
  page: number
  pageSize: 20
}
