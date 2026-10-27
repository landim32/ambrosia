import { useCallback, useRef, useState } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { PasswordInput } from '@/components/common/PasswordInput'
import { AuthMascot } from '@/components/layout/AuthMascot'
import { BrandLogo } from '@/components/layout/BrandLogo'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { returnToOf, validateRegisterForm } from '@/lib/authForm'
import { useAuth } from '@/hooks/useAuth'

import type { AuthFieldErrors, RegisterFormValues } from '@/lib/authForm'
import type { FormEvent } from 'react'

const EMPTY: RegisterFormValues = { name: '', email: '', password: '', confirmPassword: '' }

/** Ordem dos campos: o primeiro inválido recebe o foco (FR-019). */
const FIELD_ORDER = ['name', 'email', 'password', 'confirmPassword'] as const

const idOf = (field: (typeof FIELD_ORDER)[number]): string => `register-${field}`
const errorIdOf = (field: (typeof FIELD_ORDER)[number]): string => `register-${field}-error`

/**
 * Tela de cadastro `/cadastro` (FR-001, FR-003, contracts/ui-routes.md).
 *
 * Pública, fora de `RequireAuth`: quem já tem sessão é redirecionado para
 * `returnTo ?? '/clientes'` (edge case "já conectada abre /cadastro"). O 201 da
 * API já inicia a sessão, então o `<Navigate>` do render seguinte cuida do pós-
 * cadastro — a tela não navega por conta própria (US1 cenário 1 e 7).
 *
 * A senha nunca é cortada; nome e e-mail sim (FR-003). Depois de um erro as
 * senhas digitadas continuam nos campos (edge case da spec), e o formulário é
 * um `<form>` nativo: cada campo tem seu `<label htmlFor>`, então ENTER em
 * qualquer um deles envia.
 */
export const RegisterPage = () => {
  const { t } = useTranslation()
  const location = useLocation()
  const { register, isAuthenticated, loading, error, errorFields, clearError } = useAuth()

  const returnTo = returnToOf(location.state)
  const destination = returnTo ?? '/clientes'

  const [values, setValues] = useState<RegisterFormValues>(EMPTY)
  const [localErrors, setLocalErrors] = useState<AuthFieldErrors>({})
  const formRef = useRef<HTMLFormElement>(null)

  // Erro de campo: o da API (400) tem precedência sobre o local, porque descreve
  // a recusa real do servidor; na ausência dele vale o que a validação local achou.
  const errorOf = useCallback(
    (field: keyof AuthFieldErrors): string | undefined => {
      const apiMessage = field === 'confirmPassword' ? undefined : errorFields[field]

      return apiMessage ?? localErrors[field]
    },
    [errorFields, localErrors],
  )

  const setValue = useCallback(
    (field: keyof RegisterFormValues) =>
      (event: { target: { value: string } }): void => {
        setValues((current) => ({ ...current, [field]: event.target.value }))
        setLocalErrors((current) => ({ ...current, [field]: undefined }))
        clearError()
      },
    [clearError],
  )

  const handleSubmit = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault()

    const errors = validateRegisterForm(values)
    setLocalErrors(errors)

    const firstInvalid = FIELD_ORDER.find((field) => errors[field] !== undefined)

    if (firstInvalid !== undefined) {
      formRef.current
        ?.querySelector<HTMLInputElement>(`#${idOf(firstInvalid)}`)
        ?.focus()

      return
    }

    // Nome e e-mail cortados; a senha exatamente como digitada (FR-003). O sucesso
    // grava a sessão no provider e o render seguinte cai no <Navigate>.
    await register(values.name.trim(), values.email.trim(), values.password)
  }

  if (isAuthenticated) {
    return <Navigate to={destination} replace />
  }

  const hasGeneralError = error !== null && error !== ''

  const fieldProps = (field: (typeof FIELD_ORDER)[number]) => {
    const message = errorOf(field)

    return {
      'aria-invalid': message !== undefined,
      ...(message === undefined ? {} : { 'aria-describedby': errorIdOf(field) }),
    }
  }

  const hint = (field: (typeof FIELD_ORDER)[number]) => {
    const message = errorOf(field)

    return message === undefined ? null : (
      <p id={errorIdOf(field)} className="text-sm text-destructive">
        {/* Mensagem da API já vem em português; a local é uma chave i18n. */}
        {message.startsWith('validation.') ? t(message) : message}
      </p>
    )
  }

  return (
    // Mobile: logo + cartão do formulário (o mascote fica de fora para os quatro
    // campos não descerem). Desktop (lg+): mascote grande à esquerda, como no login.
    <main className="px-safe pt-safe pb-safe relative isolate flex min-h-dvh w-full items-center justify-center py-8">
      {/* Brilho azul da marca atrás da logo (decorativo). */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 top-0 -z-10 mx-auto h-72 max-w-lg rounded-full bg-brand/25 blur-3xl lg:hidden"
      />

      <div className="grid w-full max-w-md items-center lg:max-w-5xl lg:grid-cols-[minmax(0,1fr)_minmax(0,28rem)] lg:gap-16">
        <AuthMascot variant="hero" className="hidden lg:flex" />

        <div className="flex w-full min-w-0 flex-col gap-6">
          <BrandLogo decorative className="mx-auto h-9 sm:h-10 lg:mx-0" />
          <Card className="gap-4">
            <CardHeader className="gap-1">
              <h1 className="text-2xl font-bold tracking-tight text-foreground">{t('register.title')}</h1>
              <p className="text-sm text-muted-foreground">{t('register.subtitle')}</p>
            </CardHeader>

            <CardContent>
              <form
                ref={formRef}
                onSubmit={(event) => {
                  void handleSubmit(event)
                }}
                className="flex flex-col gap-4"
                noValidate
              >
                {hasGeneralError ? (
                  <p
                    role="alert"
                    className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive"
                  >
                    {error}
                  </p>
                ) : null}

                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={idOf('name')}>{t('register.name')}</Label>
                  <Input
                    id={idOf('name')}
                    name="name"
                    className="min-h-11"
                    type="text"
                    autoComplete="name"
                    maxLength={120}
                    value={values.name}
                    onChange={setValue('name')}
                    {...fieldProps('name')}
                  />
                  {hint('name')}
                </div>

                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={idOf('email')}>{t('login.email')}</Label>
                  <Input
                    id={idOf('email')}
                    name="email"
                    className="min-h-11"
                    type="email"
                    autoComplete="email"
                    maxLength={254}
                    value={values.email}
                    onChange={setValue('email')}
                    {...fieldProps('email')}
                  />
                  {hint('email')}
                </div>

                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={idOf('password')}>{t('login.password')}</Label>
                  <PasswordInput
                    id={idOf('password')}
                    name="password"
                    value={values.password}
                    onChange={setValue('password')}
                    autoComplete="new-password"
                    toggleLabels={{ show: t('common.showPassword'), hide: t('common.hidePassword') }}
                    {...fieldProps('password')}
                  />
                  {hint('password')}
                </div>

                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={idOf('confirmPassword')}>{t('register.confirmPassword')}</Label>
                  <PasswordInput
                    id={idOf('confirmPassword')}
                    name="confirmPassword"
                    value={values.confirmPassword}
                    onChange={setValue('confirmPassword')}
                    autoComplete="new-password"
                    toggleLabels={{ show: t('common.showPassword'), hide: t('common.hidePassword') }}
                    {...fieldProps('confirmPassword')}
                  />
                  {hint('confirmPassword')}
                </div>

                <Button type="submit" className="min-h-12 rounded-xl text-[0.9375rem] font-semibold" disabled={loading}>
                  {t('register.submit')}
                </Button>

                <p aria-live="polite" className="min-h-5 text-center text-xs text-muted-foreground">
                  {loading ? t('common.loading') : ''}
                </p>

                <p className="text-center text-sm text-muted-foreground">
                  <Link
                    to="/login"
                    state={{ returnTo }}
                    className="inline-flex min-h-11 items-center font-medium text-brand-soft underline-offset-4 hover:underline"
                  >
                    {t('register.haveAccount')}
                  </Link>
                </p>
              </form>
            </CardContent>
          </Card>
        </div>
      </div>
    </main>
  )
}

export default RegisterPage
