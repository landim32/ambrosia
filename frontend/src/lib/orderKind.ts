import type { OrderInfo } from '@/types/order'

/** Chave i18n do tipo do pedido (FR-021): o texto vem do recurso pt-BR. */
export const orderKindLabel = (kind: OrderInfo['kind']): string => `orders.${kind}`

/**
 * Devolução e cancelamento são os tipos "negativos" (FR-018): o total vem
 * negativo da API e a tela dá a eles destaque distinto do de venda.
 */
export const isNegativeKind = (kind: OrderInfo['kind']): boolean => kind !== 'sale'
