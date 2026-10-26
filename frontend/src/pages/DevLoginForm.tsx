import { useState } from 'react'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useAuth } from '@/hooks/useAuth'

import type { FormEvent } from 'react'

/**
 * Entrada de desenvolvimento (FR-002): `POST /auth/dev-login` com e-mail e nome.
 *
 * Este arquivo SÓ entra no bundle quando `VITE_DEV_LOGIN === 'true'`: `LoginPage`
 * só o importa por trás dessa mesma flag (research §8), então o Rollup elimina o
 * chunk na build de produção (SC-008, verificado por `npm run verify:prod`).
 */
export const DevLoginForm = () => {
  const { t } = useTranslation()
  const { loginDev, loading } = useAuth()
  const [email, setEmail] = useState('ana@exemplo.com')
  const [name, setName] = useState('Ana')

  const handleSubmit = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault()

    const trimmedEmail = email.trim()
    const trimmedName = name.trim()
    if (trimmedEmail === '' || trimmedName === '') return

    // O sucesso atualiza a sessão no AuthProvider; a tela então navega.
    await loginDev(trimmedEmail, trimmedName)
  }

  return (
    <form
      onSubmit={(event) => {
        void handleSubmit(event)
      }}
      className="flex flex-col gap-3 rounded-lg border bg-card p-4"
    >
      <h2 className="text-sm font-semibold text-muted-foreground">{t('login.dev')}</h2>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="dev-login-email">{t('login.email')}</Label>
        <Input
          id="dev-login-email"
          className="min-h-11"
          name="email"
          type="email"
          autoComplete="username"
          required
          value={email}
          onChange={(event) => {
            setEmail(event.target.value)
          }}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="dev-login-name">{t('login.name')}</Label>
        <Input
          id="dev-login-name"
          className="min-h-11"
          name="name"
          type="text"
          autoComplete="given-name"
          required
          value={name}
          onChange={(event) => {
            setName(event.target.value)
          }}
        />
      </div>

      <Button type="submit" className="min-h-11" disabled={loading}>
        {t('login.submit')}
      </Button>
    </form>
  )
}

export default DevLoginForm
