import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import {
  formatReleaseTime,
  isImportFormValid,
  validateImportForm,
  type ImportFieldErrors,
  type ImportFormValues,
  type ImportPeriod,
} from '@/lib/importForm'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

import type { ChangeEvent, FormEvent } from 'react'
import type { ImportDefaults } from '@/types/import'

interface Props {
  defaults: ImportDefaults | null
  /** Período vindo de "Repetir este bloco" (`?inicio&fim`, FR-026); vale mais que a sugestão. */
  initialPeriod?: ImportPeriod | null
  /** `true` enquanto o POST está em voo (botão desabilitado). */
  busy: boolean
  /** `true` quando já há execução ativa: um segundo início daria 409 (FR-016). */
  hasActiveRun?: boolean
  /** Mensagem do servidor (400/409/429), já em português e sem credencial. */
  serverError: string | null
  onSubmit: (values: ImportFormValues) => void
}

const EMPTY: ImportFormValues = {
  login: '',
  password: '',
  startDate: '',
  endDate: '',
  dryRun: false,
}

const DEFAULT_MAX_RANGE_DAYS = 366

const idOf = (field: keyof ImportFormValues): string => `import-${field}`
const errorIdOf = (field: keyof ImportFieldErrors): string => `${idOf(field)}-error`

/**
 * Formulário de importação (feature 006, FR-001/FR-002/FR-003/FR-007, US4-1).
 *
 * As datas vêm dos defaults da API (último pedido importado + hoje em São Paulo)
 * ou do período de "Repetir este bloco"; login e senha começam **vazios** em toda
 * abertura da tela e nunca vêm de URL nem de armazenamento.
 *
 * A senha é apagada do estado assim que o envio começa (T068, FR-007/US2-3): ela
 * não pode ficar no campo para um refresh nem para o "salvar formulário" do
 * navegador. `autoComplete="off"` e `noValidate` fecham os demais caminhos.
 *
 * O envio é bloqueado localmente com a mensagem no campo (FR-003); se a mesma
 * requisição chegar à API por fora da tela, as mesmas regras respondem 400 sem
 * ecoar valor (US1-5).
 */
export const ImportForm = ({
  defaults,
  initialPeriod = null,
  busy,
  hasActiveRun = false,
  serverError,
  onSubmit,
}: Props) => {
  const { t } = useTranslation()
  const [values, setValues] = useState<ImportFormValues>(EMPTY)
  const [errors, setErrors] = useState<ImportFieldErrors>({})
  const submittedOnce = useRef(false)
  const datesFilled = useRef(false)

  // Preenche as datas uma única vez, sem amassar o que a vendedora já digitou.
  useEffect(() => {
    if (datesFilled.current) return

    if (initialPeriod !== null) {
      datesFilled.current = true
      setValues((current) => ({ ...current, startDate: initialPeriod.start, endDate: initialPeriod.end }))

      return
    }

    if (defaults === null) return

    datesFilled.current = true
    setValues((current) => ({
      ...current,
      startDate: defaults.suggestedStartDate ?? '',
      endDate: defaults.suggestedEndDate,
    }))
  }, [defaults, initialPeriod])

  // A senha sai do estado (e do DOM) assim que o envio começa (FR-007).
  useEffect(() => {
    if (busy) setValues((current) => (current.password === '' ? current : { ...current, password: '' }))
  }, [busy])

  const maxRange = defaults?.maxRangeDays ?? DEFAULT_MAX_RANGE_DAYS

  const update = (next: ImportFormValues): void => {
    setValues(next)

    // Depois da primeira tentativa a mensagem acompanha a digitação: some quando o
    // problema é resolvido, sem exigir novo envio.
    if (submittedOnce.current) {
      setErrors(validateImportForm(next, { maxRangeDays: maxRange }))
    }
  }

  const onText =
    (field: 'login' | 'password' | 'startDate' | 'endDate') =>
    (event: ChangeEvent<HTMLInputElement>): void => {
      update({ ...values, [field]: event.target.value })
    }

  const onDryRun = (event: ChangeEvent<HTMLInputElement>): void => {
    update({ ...values, dryRun: event.target.checked })
  }

  const handleSubmit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault()
    submittedOnce.current = true

    const found = validateImportForm(values, { maxRangeDays: maxRange })

    setErrors(found)

    if (!isImportFormValid(found)) return

    onSubmit(values)
  }

  const releaseTime = formatReleaseTime(defaults?.lockedUntil ?? null)
  const locked = releaseTime !== ''
  const disabled = locked || hasActiveRun

  const fieldProps = (field: keyof ImportFieldErrors) => ({
    'aria-invalid': errors[field] !== undefined,
    ...(errors[field] === undefined ? {} : { 'aria-describedby': errorIdOf(field) }),
  })

  const hint = (field: keyof ImportFieldErrors) => {
    const message = errors[field]

    return message === undefined ? null : (
      <p id={errorIdOf(field)} className="text-sm text-destructive">
        {t(message, { max: maxRange })}
      </p>
    )
  }

  return (
    <Card className="gap-4">
      <CardHeader className="gap-1">
        <h2 className="text-lg font-semibold text-foreground">{t('import.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('import.subtitle')}</p>
        <p className="text-xs text-muted-foreground">{t('import.credentialsHelp')}</p>
      </CardHeader>

      <CardContent>
        <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
          {serverError !== null ? (
            <p
              role="alert"
              data-testid="import-server-error"
              className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive"
            >
              {serverError}
            </p>
          ) : null}

          <div className="flex flex-col gap-1.5">
            <Label htmlFor={idOf('login')}>{t('import.login')}</Label>
            <Input
              id={idOf('login')}
              name="login"
              className="min-h-11"
              type="text"
              autoComplete="off"
              data-testid="import-login"
              maxLength={120}
              value={values.login}
              onChange={onText('login')}
              disabled={disabled}
              {...fieldProps('login')}
            />
            {hint('login')}
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor={idOf('password')}>{t('import.password')}</Label>
            <Input
              id={idOf('password')}
              name="password"
              className="min-h-11"
              type="password"
              autoComplete="off"
              data-testid="import-password"
              maxLength={200}
              value={values.password}
              onChange={onText('password')}
              disabled={disabled}
              {...fieldProps('password')}
            />
            {hint('password')}
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={idOf('startDate')}>{t('import.startDate')}</Label>
              <Input
                id={idOf('startDate')}
                name="startDate"
                className="min-h-11"
                type="date"
                data-testid="import-start-date"
                value={values.startDate}
                onChange={onText('startDate')}
                disabled={disabled}
                {...fieldProps('startDate')}
              />
              {hint('startDate')}
              <p className="text-xs text-muted-foreground">
                {defaults !== null && defaults.suggestedStartDate !== null
                  ? t('import.lastImportedHelp')
                  : t('import.firstImportHelp')}
              </p>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor={idOf('endDate')}>{t('import.endDate')}</Label>
              <Input
                id={idOf('endDate')}
                name="endDate"
                className="min-h-11"
                type="date"
                data-testid="import-end-date"
                value={values.endDate}
                onChange={onText('endDate')}
                disabled={disabled}
                {...fieldProps('endDate')}
              />
              {hint('endDate')}
            </div>
          </div>

          <label className="flex min-h-11 items-start gap-3" htmlFor={idOf('dryRun')}>
            <input
              id={idOf('dryRun')}
              name="dryRun"
              type="checkbox"
              className="mt-1 size-5"
              data-testid="import-dry-run"
              checked={values.dryRun}
              onChange={onDryRun}
              disabled={disabled}
            />
            <span>
              <span className="block text-sm font-medium text-foreground">{t('import.dryRun')}</span>
              <span className="block text-xs text-muted-foreground">{t('import.dryRunHelp')}</span>
            </span>
          </label>

          <Button type="submit" className="min-h-11" disabled={busy || disabled} data-testid="import-submit">
            {busy ? t('import.starting') : t('import.start')}
          </Button>

          {locked ? (
            <p
              role="alert"
              data-testid="import-lock-message"
              className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive"
            >
              {t('import.lockMessage', { time: releaseTime })}
              <span className="mt-1 block text-xs">{t('import.lockedHelp')}</span>
            </p>
          ) : null}
        </form>
      </CardContent>
    </Card>
  )
}

export default ImportForm
