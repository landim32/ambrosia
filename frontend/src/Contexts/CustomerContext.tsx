import {
  createContext,
  useCallback,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { useTranslation } from 'react-i18next'

import { notifyError, toUserMessage } from '@/lib/errors'
import { NotFoundError, UnauthorizedError } from '@/Services/apiClient'
import { customerService } from '@/Services/customerService'

import type { PagedResult } from '@/types/common'
import type { CustomerDetailInfo, CustomerInfo, CustomerListParams } from '@/types/customer'

export interface CustomerContextType {
  /** Clientes da página corrente (`GET /customers`). */
  customers: CustomerInfo[]
  totalCount: number
  /**
   * Detalhe do cliente aberto. O CPF COMPLETO mora aqui — só em memória — e
   * `clearSelected()` o descarta ao sair da tela (FR-022, data-model §3).
   */
  selectedCustomer: CustomerDetailInfo | null
  loading: boolean
  error: string | null
  /** 404: id inexistente OU cliente de outra vendedora — mesma tela (FR-027). */
  notFound: boolean
  loadCustomers: (params: CustomerListParams) => Promise<void>
  loadCustomer: (id: number) => Promise<void>
  /** Repete a última consulta da tela (FR-025/FR-026, T086). */
  retryCustomers: () => void
  retryCustomer: () => void
  clearSelected: () => void
  clearError: () => void
}

export const CustomerContext = createContext<CustomerContextType | undefined>(undefined)

const FIRST_PAGE: PagedResult<CustomerInfo> = {
  items: [],
  page: 1,
  pageSize: 20,
  totalCount: 0,
}

/**
 * CustomerProvider - estado de consulta de clientes (research §15).
 *
 * Tratamento de erros (data-model §2): `UnauthorizedError` é IGNORADO aqui —
 * quem encerra a sessão e mostra o aviso é o `AuthContext`, e duas telas
 * avisando a mesma coisa seria ruído; `NotFoundError` vira `notFound` (tela
 * própria, sem toast); os demais viram `error` (mensagem legível) + toast com
 * "Tentar de novo" (FR-025/FR-026).
 *
 * Um contador de requisições (`useRef`) descarta a resposta da busca anterior:
 * com debounce de 400 ms duas teclas rápidas podem voltar fora de ordem, e a
 * página mostraria clientes de um termo que a vendedora já apagou (FR-009).
 *
 * O retry é responsabilidade DO CONTEXTO, não da página (T086): guardar a última
 * consulta em ref deixa `retryCustomers` com identidade estável, então o botão
 * "Tentar de novo" — na tela e no toast — repete exatamente a consulta que falhou,
 * sem a página remontar o efeito de carga.
 */
export const CustomerProvider = ({ children }: { children: ReactNode }) => {
  const { t } = useTranslation()

  const [page, setPage] = useState<PagedResult<CustomerInfo>>(FIRST_PAGE)
  const [selectedCustomer, setSelectedCustomer] = useState<CustomerDetailInfo | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notFound, setNotFound] = useState(false)

  const listToken = useRef(0)
  const detailToken = useRef(0)
  const lastListParams = useRef<CustomerListParams | null>(null)
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

  /** A chamada de lista em si; `retryCustomers` repete este caminho. */
  const runList = useCallback(
    async (params: CustomerListParams): Promise<void> => {
      listToken.current += 1
      const token = listToken.current
      setLoading(true)

      try {
        const result = await customerService.list(params)
        if (listToken.current !== token) return

        setPage(result)
        setError(null)
        setNotFound(false)
      } catch (err) {
        if (listToken.current !== token) return
        if (absorbError(err)) return

        setNotFound(false)
        setError(toUserMessage(err, t))
        notifyError(err, t, () => {
          void runList(params)
        })
      } finally {
        if (listToken.current === token) setLoading(false)
      }
    },
    [absorbError, t],
  )

  const loadCustomers = useCallback(
    async (params: CustomerListParams): Promise<void> => {
      lastListParams.current = params
      await runList(params)
    },
    [runList],
  )

  /** Idem para o perfil: o retry do detalhe não arrasta a lista atrás. */
  const runDetail = useCallback(
    async (id: number): Promise<void> => {
      detailToken.current += 1
      const token = detailToken.current
      setLoading(true)
      setNotFound(false)

      try {
        const detail = await customerService.getById(id)
        if (detailToken.current !== token) return

        setSelectedCustomer(detail)
        setError(null)
      } catch (err) {
        if (detailToken.current !== token) return
        setSelectedCustomer(null)
        if (absorbError(err)) return

        setError(toUserMessage(err, t))
        notifyError(err, t, () => {
          void runDetail(id)
        })
      } finally {
        if (detailToken.current === token) setLoading(false)
      }
    },
    [absorbError, t],
  )

  const loadCustomer = useCallback(
    async (id: number): Promise<void> => {
      lastDetailId.current = id
      await runDetail(id)
    },
    [runDetail],
  )

  const retryCustomers = useCallback((): void => {
    const params = lastListParams.current
    if (params === null) return

    setError(null)
    void runList(params)
  }, [runList])

  const retryCustomer = useCallback((): void => {
    const id = lastDetailId.current
    if (id === null) return

    setError(null)
    void runDetail(id)
  }, [runDetail])

  /** Devolve o CPF completo ao nada: o perfil fecha e o estado vai junto. */
  const clearSelected = useCallback((): void => {
    detailToken.current += 1
    setSelectedCustomer(null)
    setNotFound(false)
    setError(null)
  }, [])

  const value = useMemo<CustomerContextType>(
    () => ({
      customers: page.items,
      totalCount: page.totalCount,
      selectedCustomer,
      loading,
      error,
      notFound,
      loadCustomers,
      loadCustomer,
      retryCustomers,
      retryCustomer,
      clearSelected,
      clearError,
    }),
    [
      page,
      selectedCustomer,
      loading,
      error,
      notFound,
      loadCustomers,
      loadCustomer,
      retryCustomers,
      retryCustomer,
      clearSelected,
      clearError,
    ],
  )

  return <CustomerContext.Provider value={value}>{children}</CustomerContext.Provider>
}

export default CustomerContext
