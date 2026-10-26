import { lazy, Suspense, useCallback } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { useAuth } from '@/hooks/useAuth'
import { useGoogleIdentity } from '@/hooks/useGoogleIdentity'

/**
 * `state` levado por `RequireAuth` e por `AuthContext.logout` ao redirecionar.
 * Vive neste módulo (e não em `RequireAuth`) para a tela de entrada não importar
 * a guarda de rotas — e, com ela, os contextos e serviços de dados internos.
 */
interface ReturnToState {
  returnTo?: string
}

/**
 * Entrada de desenvolvimento sob a flag de build (research §8, FR-002, SC-008).
 *
 * `import.meta.env.VITE_DEV_LOGIN` é substituída pelo Vite em TEMPO DE BUILD: na
 * build de produção o ternário abaixo colapsa para `null` e o Rollup elimina o
 * `lazy()` — e portanto o chunk de `DevLoginForm` — por completo.
 *
 * Nada de `import DevLoginForm from './DevLoginForm'` para o `Suspense`: o import
 * estático (mesmo do tipo "só o nome", usado em `React.lazy`) é marcado como
 * usado ao referenciar o binding e NÃO é eliminado com o ramo morto — o chunk
 * sobrevivia à flag. O lazy() é a única referência, e o `<Suspense>` já existe em
 * volta dele. Verificação: `npm run verify:prod` (SC-008).
 */
const DevLoginForm =
  import.meta.env.VITE_DEV_LOGIN === 'true' ? lazy(() => import('./DevLoginForm')) : null

const returnToOf = (state: unknown): string | undefined => {
  if (typeof state !== 'object' || state === null) return undefined

  const { returnTo } = state as ReturnToState

  return typeof returnTo === 'string' && returnTo !== '' ? returnTo : undefined
}

/**
 * Tela de entrada (FR-002/FR-004/FR-007): botão do Google Identity Services e,
 * fora de produção, o formulário de desenvolvimento. Quem já tem sessão volta
 * para a rota que originou o redirecionamento (`state.returnTo`) ou `/clientes`.
 * O aviso de expiração vem do parâmetro `motivo=expirada` (não de toast), para
 * sobreviver ao redirecionamento (research §5).
 */
export const LoginPage = () => {
  const { t } = useTranslation()
  const location = useLocation()
  const { isAuthenticated, loginWithGoogle, loading, error, expiryNotice } = useAuth()

  const returnTo = returnToOf(location.state)
  const expired = new URLSearchParams(location.search).get('motivo') === 'expirada' || expiryNotice
  const destination = returnTo ?? '/clientes'

  const handleCredential = useCallback(
    (credential: string): void => {
      // O sucesso grava a sessão no provider; o render seguinte cai no <Navigate>.
      void loginWithGoogle(credential)
    },
    [loginWithGoogle],
  )

  const googleButtonRef = useGoogleIdentity(handleCredential)
  const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID

  if (isAuthenticated) {
    return <Navigate to={destination} replace />
  }

  return (
    <main className="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center gap-4 px-4 py-8">
      <header className="flex flex-col gap-1 text-center">
        <h1 className="text-xl font-semibold text-foreground">{t('login.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('login.subtitle')}</p>
      </header>

      {expired ? (
        <p
          role="alert"
          className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-center text-sm text-destructive"
        >
          {t('login.expired')}
        </p>
      ) : null}

      {error !== null && error !== '' ? (
        <p className="text-center text-sm text-destructive">{error}</p>
      ) : null}

      <div ref={googleButtonRef} data-testid="google-button" className="flex min-h-11 justify-center">
        {googleClientId === undefined || googleClientId.trim() === '' ? (
          <Button type="button" variant="outline" className="w-full min-h-11" disabled>
            {t('login.google')}
          </Button>
        ) : null}
      </div>

      {DevLoginForm === null ? null : (
        <Suspense fallback={null}>
          <DevLoginForm />
        </Suspense>
      )}

      <p aria-live="polite" className="min-h-5 text-center text-xs text-muted-foreground">
        {loading ? t('common.loading') : ''}
      </p>
    </main>
  )
}

export default LoginPage
