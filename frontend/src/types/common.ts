/** Common Types — tipos compartilhados entre as entidades (data-model §1) */

/** Resultado paginado genérico da API (`PagedCustomerInfo` / `PagedOrderInfo`). */
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

/** Erro de campo devolvido pela API (chaves em português, constituição §IV). */
export interface FieldError {
  campo: string
  mensagem: string
}

/** Corpo de erro da API em 400/404/409. */
export interface ErrorResult {
  sucesso: false
  mensagem: string
  erros: FieldError[]
}

/** Tamanho de página fixo da API (contratos/api-consumption.md). */
export const PAGE_SIZE = 20
