import { useCallback, useRef, useState } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { GoogleSignInButton } from '@/components/common/GoogleSignInButton'
import { PasswordInput } from '@/components/common/PasswordInput'
import { AuthMascot } from '@/components/layout/AuthMascot'
import { BrandLogo } from '@/components/layout/BrandLogo'
import { returnToOf, validateLoginForm } from '@/lib/authForm'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Separator } from '@/components/ui/separator'
import { useAuth } from '@/hooks/useAuth'
import { useGoogleIdentity } from '@/hooks/useGoogleIdentity'

import type { LoginFormValues } from '@/lib/authForm'
import type { FormEvent } from 'react'

const EMPTY: LoginFormValues = { email: '', password: '' }

/**
 * Tela de entrada `/login` (FR-002/FR-004/FR-007/FR-018).
 *
 * Ordem do `contracts/ui-routes.md`: botão do Google (quando o cliente existe) →
 * separador "ou" → formulário de e-mail/senha → link "Não tem conta? Criar
 * conta". O aviso de expiração vem do parâmetro `motivo=expirada` (não de toast),
 * para sobreviver ao redirecionamento (research §5); o erro de credencial (401)
 * usa o MESMO `role="alert"`, e a senha digitada permanece no campo.
 *
 * Quem já tem sessão volta para `state.returnTo ?? '/clientes'`. Não existe mais
 * formulário de desenvolvimento: a 003 removeu a rota, o componente e a flag de
 * build de todos os ambientes (FR-012/FR-013).
 */
export const LoginPage = () => {
  const { t } = useTranslation()
  const location = useLocation()
  const {
    isAuthenticated,
    loginWithGoogle,
    loginWithPassword,
    loading,
    error,
    errorFields,
    clearError,
    expiryNotice,
  } = useAuth()

  const returnTo = returnToOf(location.state)
  const expired = new URLSearchParams(location.search).get('motivo') === 'expirada' || expiryNotice
  const destination = returnTo ?? '/clientes'

  const [values, setValues] = useState<LoginFormValues>(EMPTY)
  const [localErrors, setLocalErrors] = useState<Partial<Record<'email' | 'password', string>>>({})
  const formRef = useRef<HTMLFormElement>(null)

  const handleCredential = useCallback(
    (credential: string): void => {
      // O sucesso grava a sessão no provider; o render seguinte cai no <Navigate>.
      void loginWithGoogle(credential)
    },
    [loginWithGoogle],
  )

  const googleButtonRef = useGoogleIdentity(handleCredential)
  const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID
  const hasGoogle = googleClientId !== undefined && googleClientId.trim() !== ''

  const setValue = useCallback(
    (field: keyof LoginFormValues) =>
      (event: { target: { value: string } }): void => {
        setValues((current) => ({ ...current, [field]: event.target.value }))
        setLocalErrors((current) => ({ ...current, [field]: undefined }))
        clearError()
      },
    [clearError],
  )

  const handleSubmit = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault()

    const errors = validateLoginForm(values)
    setLocalErrors(errors)

    const firstInvalidId =
      errors.email !== undefined
        ? 'login-email'
        : errors.password !== undefined
          ? 'login-password'
          : undefined

    if (firstInvalidId !== undefined) {
      formRef.current?.querySelector<HTMLInputElement>(`#${firstInvalidId}`)?.focus()

      return
    }

    // E-mail cortado, senha como digitada (FR-002). O sucesso grava a sessão e o
    // render seguinte cai no <Navigate>; o 401 fica em `error` (research §3).
    await loginWithPassword(values.email.trim(), values.password)
  }

  if (isAuthenticated) {
    return <Navigate to={destination} replace />
  }

  const hasGeneralError = expired || (error !== null && error !== '')

  const messageOf = (field: 'email' | 'password'): string | undefined => {
    const apiMessage = errorFields[field]

    return apiMessage ?? localErrors[field]
  }

  const emailMessage = messageOf('email')
  const passwordMessage = messageOf('password')

  return (
    // Mobile: uma coluna — mascote pequeno, logo, título e formulário (cabe em
    // 375×667 sem rolar). Desktop (lg+): mascote grande à esquerda, formulário à direita.
    <main className="px-safe pt-safe pb-safe relative isolate flex min-h-dvh w-full items-center justify-center py-6 sm:py-8 [@media(max-height:700px)]:py-4">
      {/* Brilho azul da marca atrás do topo (decorativo). */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 top-0 -z-10 mx-auto h-80 max-w-lg rounded-full bg-brand/30 blur-3xl lg:hidden"
      />

      <div className="grid w-full max-w-md items-center lg:max-w-5xl lg:grid-cols-[minmax(0,1fr)_minmax(0,28rem)] lg:gap-16">
        <AuthMascot variant="hero" className="hidden lg:flex" />

        <div className="flex w-full min-w-0 flex-col gap-4 sm:gap-5 [@media(max-height:700px)]:gap-3">
          <header className="flex flex-col items-center gap-2 text-center lg:items-start lg:text-left">
            <AuthMascot variant="compact" className="mb-1 lg:hidden" />
            <BrandLogo decorative className="mb-3 h-9 sm:h-11 lg:mb-6 lg:h-12 [@media(max-height:700px)]:mb-1" />
            <h1 className="text-2xl font-bold tracking-tight text-foreground">{t('login.title')}</h1>
            <p className="text-sm text-muted-foreground">{t('login.subtitle')}</p>
          </header>

          {hasGeneralError ? (
            <p
              role="alert"
              className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-center text-sm text-destructive"
            >
              {expired ? t('login.expired') : error}
            </p>
          ) : null}

          {hasGoogle ? (
            <>
              <GoogleSignInButton ref={googleButtonRef} />
              <div className="flex items-center gap-3">
                <Separator className="flex-1" />
                <span className="text-xs uppercase text-muted-foreground">{t('login.or')}</span>
                <Separator className="flex-1" />
              </div>
            </>
          ) : null}

          <form
            ref={formRef}
            onSubmit={(event) => {
              void handleSubmit(event)
            }}
            className="flex flex-col gap-4 [@media(max-height:700px)]:gap-3"
            noValidate
          >
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="login-email">{t('login.email')}</Label>
              <Input
                id="login-email"
                name="email"
                className="min-h-11"
                type="email"
                autoComplete="username"
                value={values.email}
                onChange={setValue('email')}
                aria-invalid={emailMessage !== undefined}
                {...(emailMessage === undefined ? {} : { 'aria-describedby': 'login-email-error' })}
              />
              {emailMessage === undefined ? null : (
                <p id="login-email-error" className="text-sm text-destructive">
                  {emailMessage.startsWith('validation.') ? t(emailMessage) : emailMessage}
                </p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="login-password">{t('login.password')}</Label>
              <PasswordInput
                id="login-password"
                name="password"
                value={values.password}
                onChange={setValue('password')}
                autoComplete="current-password"
                toggleLabels={{ show: t('common.showPassword'), hide: t('common.hidePassword') }}
                aria-invalid={passwordMessage !== undefined}
                {...(passwordMessage === undefined
                  ? {}
                  : { 'aria-describedby': 'login-password-error' })}
              />
              {passwordMessage === undefined ? null : (
                <p id="login-password-error" className="text-sm text-destructive">
                  {passwordMessage.startsWith('validation.') ? t(passwordMessage) : passwordMessage}
                </p>
              )}
            </div>

            <Button type="submit" className="min-h-12 rounded-xl text-[0.9375rem] font-semibold" disabled={loading}>
              {t('login.submit')}
            </Button>

            <p aria-live="polite" className="min-h-5 text-center text-xs text-muted-foreground">
              {loading ? t('common.loading') : ''}
            </p>
          </form>

          {/* FR-018 / US1 cenário 7: a entrada é o caminho para o cadastro. */}
          <p className="text-center text-sm text-muted-foreground lg:text-left">
            {t('login.noAccount')}{' '}
            <Link
              to="/cadastro"
              state={{ returnTo }}
              className="inline-flex min-h-11 items-center font-medium text-brand-soft underline-offset-4 hover:underline"
            >
              {t('login.createAccount')}
            </Link>
          </p>
        </div>
      </div>
    </main>
  )
}

export default LoginPage
