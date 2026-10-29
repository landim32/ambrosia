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
import { ApiError, UnauthorizedError } from '@/Services/apiClient'
import { importService } from '@/Services/importService'

import type { MaisVendasCredentials, MaisVendasCredentialsRequest } from '@/types/import'

/**
 * Estado da tela de configuração do +Vendas (feature 008, US1, FR-001 a FR-007,
 * research R4/R10).
 *
 * Contexto SEPARADO do `ImportContext` de propósito (skill `react-architecture`:
 * uma entidade por contexto): a importação não precisa das credenciais — ela
 * precisa saber SE elas existem, e isso já vem pronto em
 * `GET /imports/mais-vendas/defaults` (`hasCredentials`/`credentialsStatus`,
 * research R5). Puxar as credenciais para o contexto da importação criaria uma
 * segunda fonte da mesma verdade, e as duas divergiriam depois de um `PUT`.
 *
 * **A senha nunca entra neste contexto** (FR-003, SC-003):
 * `MaisVendasCredentials` é o DTO de saída do servidor e não tem campo de senha —
 * nem mascarada —, então é impossível a senha ficar retida em estado global. O
 * valor digitado vive apenas no `useState` local do cartão e é descartado assim
 * que o `PUT` termina, em qualquer desfecho. Nada aqui escreve em `localStorage`,
 * `sessionStorage` ou cookie.
 *
 * `error` guarda a mensagem do SERVIDOR (400 "Informe a senha para trocar o
 * login.", contracts/api.md) reutilizada literalmente na tela, como `ImportContext`
 * faz com o 409/429 — nenhuma dessas mensagens ecoa a senha.
 */
export interface MaisVendasSettingsContextType {
  /** Resposta de `GET /imports/mais-vendas/credentials`; `null` antes da primeira leitura. */
  credentials: MaisVendasCredentials | null
  loading: boolean
  /** `true` durante o `PUT` (desabilita salvar). */
  saving: boolean
  /** `true` durante o `DELETE` (desabilita remover). */
  removing: boolean
  error: string | null
  loadCredentials: () => Promise<void>
  /** `true` quando o servidor aceitou — a tela só limpa o campo de senha nesse caso. */
  saveCredentials: (request: MaisVendasCredentialsRequest) => Promise<boolean>
  /** `true` quando removeu (a rota é idempotente, FR-006). */
  removeCredentials: () => Promise<boolean>
  clearError: () => void
}

export const MaisVendasSettingsContext = createContext<MaisVendasSettingsContextType | undefined>(undefined)

/** Estado "sem credenciais" para o `DELETE` que devolve `204` em vez do corpo. */
const NO_CREDENTIALS: MaisVendasCredentials = {
  login: null,
  hasPassword: false,
  savedAt: null,
  passwordChangedAt: null,
  status: 'none',
}

/**
 * `MaisVendasSettingsProvider`. O `mounted` existe porque o provider vive em
 * `main.tsx` também em `/login` (sem sessão): nenhuma leitura é disparada aqui — é
 * a tela que chama `loadCredentials` ao montar — e um `PUT` que termina depois de a
 * vendedora sair da tela não pode escrever estado de tela fantasma.
 */
export const MaisVendasSettingsProvider = ({ children }: { children: ReactNode }) => {
  const { t } = useTranslation()

  const [credentials, setCredentials] = useState<MaisVendasCredentials | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [removing, setRemoving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const mounted = useRef(true)

  const clearError = useCallback((): void => {
    setError(null)
  }, [])

  /** 401 tem dono: quem desloga e avisa é o `AuthContext` (research §5/§6). */
  const absorbError = useCallback((err: unknown): boolean => err instanceof UnauthorizedError, [])

  const loadCredentials = useCallback(async (): Promise<void> => {
    setLoading(true)

    try {
      const result = await importService.getCredentials()

      if (!mounted.current) return

      setCredentials(result)
      setError(null)
    } catch (err) {
      if (!mounted.current) return
      if (absorbError(err)) return

      setError(toUserMessage(err, t))
      notifyError(err, t, () => {
        void loadCredentials()
      })
    } finally {
      if (mounted.current) setLoading(false)
    }
  }, [absorbError, t])

  const saveCredentials = useCallback(
    async (request: MaisVendasCredentialsRequest): Promise<boolean> => {
      setSaving(true)
      setError(null)

      try {
        const result = await importService.saveCredentials(request)

        if (!mounted.current) return true

        // O estado passa a ser o que o servidor devolve — inclusive `status`, que
        // volta a `valid` quando uma senha nova foi cadastrada (data-model §1). A
        // resposta não tem senha, então o que fica em memória também não tem (FR-003).
        setCredentials(result)

        return true
      } catch (err) {
        if (!mounted.current) return false

        // 400 de validação: a mensagem do servidor É a tela. `erros[]` por campo não
        // substitui a frase única — as regras de "havia credenciais?" e "o login é o
        // mesmo?" só o servidor sabe aplicar, e repeti-las aqui dividiria a regra com
        // risco de divergir (SC-005).
        if (err instanceof ApiError) {
          setError(err.mensagem)

          return false
        }

        if (absorbError(err)) return false

        setError(toUserMessage(err, t))
        notifyError(err, t)

        return false
      } finally {
        if (mounted.current) setSaving(false)
      }
    },
    [absorbError, t],
  )

  const removeCredentials = useCallback(async (): Promise<boolean> => {
    setRemoving(true)
    setError(null)

    try {
      const result = await importService.removeCredentials()

      if (!mounted.current) return true

      // A rota devolve a forma do `GET` já zerada; se o ambiente responder `204`
      // (`null`), o estado é o vazio explícito — o efeito na tela é o mesmo: sem
      // credenciais, `status: 'none'` (FR-006).
      setCredentials(result ?? NO_CREDENTIALS)

      return true
    } catch (err) {
      if (!mounted.current) return false
      if (absorbError(err)) return false

      setError(toUserMessage(err, t))
      notifyError(err, t)

      return false
    } finally {
      if (mounted.current) setRemoving(false)
    }
  }, [absorbError, t])

  useEffect(() => {
    mounted.current = true

    return () => {
      mounted.current = false
    }
  }, [])

  const value = useMemo<MaisVendasSettingsContextType>(
    () => ({
      credentials,
      loading,
      saving,
      removing,
      error,
      loadCredentials,
      saveCredentials,
      removeCredentials,
      clearError,
    }),
    [
      credentials,
      loading,
      saving,
      removing,
      error,
      loadCredentials,
      saveCredentials,
      removeCredentials,
      clearError,
    ],
  )

  return <MaisVendasSettingsContext.Provider value={value}>{children}</MaisVendasSettingsContext.Provider>
}

export default MaisVendasSettingsContext
