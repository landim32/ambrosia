import {
  createContext,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { toUserMessage } from '@/lib/errors'
import { setUnauthorizedHandler } from '@/Services/apiClient'
import { authService } from '@/Services/authService'
import { clearSession, peekSession, writeSession } from '@/Services/authStorage'

import type { AuthSession } from '@/types/auth'

/** Motivo levado para `/login` quando a sessão acaba sem a vendedora pedir. */
export type LogoutReason = 'expirada'

export interface AuthContextType {
  /** Sessão corrente (`null` = anônima). É a única fonte de identidade da UI. */
  session: AuthSession | null
  isAuthenticated: boolean
  /** `true` enquanto uma troca de credencial está em voo. */
  loading: boolean
  /** Mensagem legível do último erro de autenticação. */
  error: string | null
  /**
   * `true` quando a sessão deixou de existir POR expiração (chave corrompida,
   * `expiresAt` vencido ou 401) — e o que faz `/login` mostrar "Sua sessão
   * expirou" mesmo quando a rota de destino chega pela guarda de rotas
   * (FR-004). Zerada em um login novo.
   */
  expiryNotice: boolean
  /** Troca o ID token do Google pelo JWT da API. `true` quando autenticou. */
  loginWithGoogle: (idToken: string) => Promise<boolean>
  /** `POST /auth/dev-login` (Development/Homolog). `true` quando autenticou. */
  loginDev: (email: string, name: string) => Promise<boolean>
  /** Encerra a sessão; com motivo, avisa a vendedora em `/login?motivo=expirada`. */
  logout: (reason?: LogoutReason) => void
  clearError: () => void
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined)

/** `setTimeout` só aceita até ~24,8 dias; o token da API dura 8 h. */
const MAX_TIMEOUT_MS = 2_147_483_647

/** Constrói a sessão a partir da resposta de login (data-model §1). */
const toSession = (result: { token: string; expiresAt: string; user: AuthSession['user'] }) => ({
  token: result.token,
  expiresAt: result.expiresAt,
  user: result.user,
})

/**
 * AuthProvider - sessão da vendedora (research §5, data-model §3).
 *
 * A expiração é verificada em três pontos: (a) ao montar (`peekSession`, que
 * também decide se há aviso), (b) antes de cada requisição (`apiClient`) e (c)
 * por um timer agendado para `expiresAt`. Os três convergem em
 * `logout('expirada')`, que limpa o storage e leva `/login?motivo=expirada`
 * guardando a rota de origem em `state.returnTo`.
 */
export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const location = useLocation()

  // A leitura inicial é não destrutiva para saber se havia chave a ser descartada.
  const initialPeek = useMemo(() => peekSession(), [])
  const [session, setSession] = useState<AuthSession | null>(initialPeek.session)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // O aviso só existe quando havia chave E ela não produziu sessão válida:
  // sessão boa no storage não é "sessão expirada".
  const [expiryNotice, setExpiryNotice] = useState(
    initialPeek.keyWasPresent && initialPeek.session === null,
  )

  const clearError = useCallback((): void => {
    setError(null)
  }, [])

  const logout = useCallback(
    (reason?: LogoutReason): void => {
      clearSession()
      setSession(null)
      setExpiryNotice(reason === 'expirada')

      const from = `${location.pathname}${location.search}`
      const target = reason === 'expirada' ? '/login?motivo=expirada' : '/login'

      navigate(
        target,
        reason === 'expirada'
          ? { replace: true, state: { returnTo: from } }
          : { replace: true },
      )
    },
    [location.pathname, location.search, navigate],
  )

  const runLogin = useCallback(
    async (request: () => Promise<ReturnType<typeof toSession>>): Promise<boolean> => {
      try {
        setLoading(true)
        setError(null)
        const next = toSession(await request())
        writeSession(next)
        setSession(next)
        setExpiryNotice(false)

        return true
      } catch (err) {
        setError(toUserMessage(err, t))

        return false
      } finally {
        setLoading(false)
      }
    },
    [t],
  )

  const loginWithGoogle = useCallback(
    (idToken: string): Promise<boolean> => runLogin(() => authService.loginWithGoogle(idToken)),
    [runLogin],
  )

  const loginDev = useCallback(
    (email: string, name: string): Promise<boolean> =>
      runLogin(() => authService.loginDev(email, name)),
    [runLogin],
  )

  // (a) Chave existe mas está corrompida/vencida: expira sem chamar a API.
  // O aviso sai uma única vez por montagem: depois da navegação, location
  // muda, logout ganha nova identidade e o efeito rodaria de novo já em
  // /login?motivo=expirada — o que gravaria /login como rota de origem.
  const announcedRef = useRef(false)

  useEffect(() => {
    if (announcedRef.current) return

    if (initialPeek.keyWasPresent && initialPeek.session === null) {
      announcedRef.current = true
      logout('expirada')
    }
  }, [initialPeek, logout])

  // (c) Timer para o instante de expiração do token.
  useEffect(() => {
    if (!session) return

    const expiresAt = Date.parse(session.expiresAt)
    if (Number.isNaN(expiresAt)) return

    const delay = Math.min(Math.max(expiresAt - Date.now(), 0), MAX_TIMEOUT_MS)
    const timer = setTimeout(() => {
      logout('expirada')
    }, delay)

    return () => {
      clearTimeout(timer)
    }
  }, [session, logout])

  // (b) 401 do apiClient: o transporte avisa a sessão (research §6).
  useEffect(() => {
    setUnauthorizedHandler(() => {
      logout('expirada')
    })

    return () => {
      setUnauthorizedHandler(undefined)
    }
  }, [logout])

  const value = useMemo<AuthContextType>(
    () => ({
      session,
      isAuthenticated: session !== null,
      loading,
      error,
      expiryNotice,
      loginWithGoogle,
      loginDev,
      logout,
      clearError,
    }),
    [session, loading, error, expiryNotice, loginWithGoogle, loginDev, logout, clearError],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export default AuthContext
