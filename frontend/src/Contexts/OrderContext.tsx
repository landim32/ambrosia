import {
  createContext,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { useTranslation } from 'react-i18next'

import { notifyError, toUserMessage } from '@/lib/errors'
import { NotFoundError, UnauthorizedError } from '@/Services/apiClient'
import { orderService } from '@/Services/orderService'

import type { OrderDetailInfo, OrderFilters, OrderInfo } from '@/types/order'
import type { PagedResult } from '@/types/common'

export interface OrderContextType {
  /** Pedidos da página corrente (`GET /orders`, data decrescente). */
  orders: OrderInfo[]
  totalCount: number
  /** Detalhe do pedido aberto (`GET /orders/{id}`). */
  selectedOrder: OrderDetailInfo | null
  loading: boolean
  error: string | null
  /** 404: id inexistente OU pedido de outra vendedora — mesma tela (FR-027). */
  notFound: boolean
  loadOrders: (filters: OrderFilters) => Promise<void>
  loadOrder: (id: number) => Promise<void>
  /** Repete a última consulta da tela (FR-025/FR-026, T086). */
  retryOrders: () => void
  retryOrder: () => void
  clearSelected: () => void
  clearError: () => void
}

export const OrderContext = createContext<OrderContextType | undefined>(undefined)

const FIRST_PAGE: PagedResult<OrderInfo> = {
  items: [],
  page: 1,
  pageSize: 20,
  totalCount: 0,
}

/**
 * OrderProvider - estado de consulta de pedidos (research §15).
 *
 * Mesmo contrato de erros do `CustomerContext` (data-model §2): 401 é IGNORADO
 * (o `AuthContext` encerra a sessão), 404 vira `notFound` (tela própria, sem
 * toast) e o resto vira `error` + toast com "Tentar de novo".
 *
 * O contador de requisições descarta a resposta do filtro anterior: com debounce
 * de 400 ms, duas mudanças rápidas de `canal`/`busca` podem voltar fora de ordem,
 * e a lista mostraria pedidos de um filtro que já saiu da URL (FR-014/FR-015).
 *
 * O retry vive AQUI, com a última consulta guardada em ref (T086): `retryOrders`
 * tem identidade estável, então o "Tentar de novo" da tela e o do toast repetem
 * a mesma consulta sem a página remontar o efeito de carga e sem recomeçar a
 * digitação do filtro.
 */
export const OrderProvider = ({ children }: { children: ReactNode }) => {
  const { t } = useTranslation()
  // Identidade instável: vive num ref para os callbacks ficarem estáveis e o
  // efeito de carga da página não reexecutar para sempre.
  const translateRef = useRef(t)

  useEffect(() => {
    translateRef.current = t
  }, [t])

  const [page, setPage] = useState<PagedResult<OrderInfo>>(FIRST_PAGE)
  const [selectedOrder, setSelectedOrder] = useState<OrderDetailInfo | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notFound, setNotFound] = useState(false)

  const listToken = useRef(0)
  const detailToken = useRef(0)
  const lastFilters = useRef<OrderFilters | null>(null)
  const lastDetailId = useRef<number | null>(null)

  const clearError = useCallback((): void => {
    setError(null)
  }, [])

  /** `true` quando o erro já tem tela própria (404) ou dono outro contexto. */
  const absorbError = useCallback((err: unknown): boolean => {
    if (err instanceof NotFoundError) {
      setNotFound(true)
      setError(null)

      return true
    }

    if (err instanceof UnauthorizedError) return true

    return false
  }, [])

  /** A chamada de lista em si; `retryOrders` repete exatamente este caminho. */
  const runList = useCallback(
    async (filters: OrderFilters): Promise<void> => {
      listToken.current += 1
      const token = listToken.current
      setLoading(true)

      try {
        const result = await orderService.list(filters)
        if (listToken.current !== token) return

        setPage(result)
        setError(null)
        setNotFound(false)
      } catch (err) {
        if (listToken.current !== token) return
        if (absorbError(err)) return

        setNotFound(false)
        setError(toUserMessage(err, translateRef.current))
        notifyError(err, translateRef.current, () => {
          void runList(filters)
        })
      } finally {
        if (listToken.current === token) setLoading(false)
      }
    },
    [absorbError],
  )

  const loadOrders = useCallback(
    async (filters: OrderFilters): Promise<void> => {
      lastFilters.current = filters
      await runList(filters)
    },
    [runList],
  )

  const runDetail = useCallback(
    async (id: number): Promise<void> => {
      detailToken.current += 1
      const token = detailToken.current
      setLoading(true)
      setNotFound(false)

      try {
        const detail = await orderService.getById(id)
        if (detailToken.current !== token) return

        setSelectedOrder(detail)
        setError(null)
      } catch (err) {
        if (detailToken.current !== token) return
        setSelectedOrder(null)
        if (absorbError(err)) return

        setError(toUserMessage(err, translateRef.current))
        notifyError(err, translateRef.current, () => {
          void runDetail(id)
        })
      } finally {
        if (detailToken.current === token) setLoading(false)
      }
    },
    [absorbError],
  )

  const loadOrder = useCallback(
    async (id: number): Promise<void> => {
      lastDetailId.current = id
      await runDetail(id)
    },
    [runDetail],
  )

  const retryOrders = useCallback((): void => {
    const filters = lastFilters.current
    if (filters === null) return

    setError(null)
    void runList(filters)
  }, [runList])

  const retryOrder = useCallback((): void => {
    const id = lastDetailId.current
    if (id === null) return

    setError(null)
    void runDetail(id)
  }, [runDetail])

  const clearSelected = useCallback((): void => {
    detailToken.current += 1
    setSelectedOrder(null)
    setNotFound(false)
    setError(null)
  }, [])

  const value = useMemo<OrderContextType>(
    () => ({
      orders: page.items,
      totalCount: page.totalCount,
      selectedOrder,
      loading,
      error,
      notFound,
      loadOrders,
      loadOrder,
      retryOrders,
      retryOrder,
      clearSelected,
      clearError,
    }),
    [
      page,
      selectedOrder,
      loading,
      error,
      notFound,
      loadOrders,
      loadOrder,
      retryOrders,
      retryOrder,
      clearSelected,
      clearError,
    ],
  )

  return <OrderContext.Provider value={value}>{children}</OrderContext.Provider>
}

export default OrderContext
