/** Customer Types — cliente da vendedora (data-model §1) */

import type { CustomerProfilePhotoInfo } from '@/types/customerPhoto'

/** Cliente em listagens: CPF sempre mascarado. */
export interface CustomerInfo {
  id: number
  name: string
  /** Formato da API, ex.: `***.456.789-**`. */
  cpfMasked: string
  createdAt: string
  updatedAt: string
  /**
   * Foto de perfil (feature 009, FR-016): o recorte quadrado gerado no
   * navegador, exibido em círculo. `null`/ausente = a cliente aparece com as
   * iniciais. Na listagem vem sem `originalUrl` (uma URL assinada por linha).
   */
  profilePhoto?: CustomerProfilePhotoInfo | null
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
  /** Fotos do álbum (feature 009, data-model §6); contagem por `COUNT`, sem coluna. */
  photoCount: number
}

/** Parâmetros de `GET /customers`. */
export interface CustomerListParams {
  search?: string
  page: number
  pageSize: 20
}
