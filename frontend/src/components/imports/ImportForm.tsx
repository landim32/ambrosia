import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { ConfirmModal } from '@/components/common/ConfirmModal'
import { Modal } from '@/components/common/Modal'
import { MAIS_VENDAS_SETTINGS_PATH } from '@/components/layout/navItems'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  formatImportDate,
  formatReleaseTime,
  isImportFormValid,
  todayInSaoPaulo,
  validateImportForm,
  type ImportFieldErrors,
  type ImportFormValues,
  type ImportPeriod,
} from '@/lib/importForm'

import type { ChangeEvent, FormEvent } from 'react'
import type { ImportDefaults } from '@/types/import'

/**
 * Fallback do limite em meses enquanto os defaults não chegam (FR-010): o MESMO
 * número do padrão do servidor (`MaisVendas:MaxRangeMonths`), para a tela não
 * aceitar um período que o `POST` recusaria na primeira leitura.
 */
const DEFAULT_MAX_RANGE_MONTHS = 3

interface Props {
  defaults: ImportDefaults | null
  /** Período vindo de "Repetir este bloco" (`?inicio&fim`, FR-026); vale mais que a sugestão. */
  initialPeriod?: ImportPeriod | null
  /** `true` enquanto o `POST` está em voo (botões desabilitados). */
  busy: boolean
  /** `true` quando já há execução ativa: um segundo início daria 409 (FR-016). */
  hasActiveRun?: boolean
  /** Mensagem do servidor (400/409/429), já em português e sem credencial. */
  serverError: string | null
  onSubmit: (values: ImportFormValues) => void
  /** "Atualizar base" (FR-013): o período é o dos defaults, só o modo é escolha da tela. */
  onUpdateBase: (dryRun: boolean) => void
}

const EMPTY: ImportFormValues = { startDate: '', endDate: '', dryRun: false }

const idOf = (field: 'startDate' | 'endDate' | 'dryRun'): string => `import-${field}`
const errorIdOf = (field: keyof ImportFieldErrors): string => `${idOf(field)}-error`

/**
 * Formulário de importação (feature 006, FR-001/FR-002/FR-003; feature 008,
 * FR-009/FR-010/FR-013 a FR-015, US2-1/US2-2, US3-1).
 *
 * **Saem os campos de login e senha** (FR-009): o início usa as credenciais
 * GUARDADAS, cadastradas em `/configuracoes/mais-vendas`. Nenhuma string, estado,
 * URL ou storage deste componente conhece uma senha — a garantia deixou de ser do
 * cliente ("não guardamos") e passou a ser do contrato (`ImportRunStartRequest` não
 * tem o campo, e um campo extra é recusado com 400).
 *
 * Sem credenciais cadastradas o formulário não é desenhado: entra o aviso com o
 * botão "Ir para a configuração" e o iniciar fica indisponível (contracts/ui.md,
 * FR-009). `needsUpdate` é o contrário — aviso com o mesmo link e o iniciar
 * disponível, porque o +Vendas pode aceitar a senha de novo (FR-007, research R5).
 *
 * As datas vêm dos defaults da API (último pedido importado + hoje em São Paulo)
 * ou do período de "Repetir este bloco", e o limite é em MESES corridos com a
 * MESMA conta do validator do servidor (`lib/importForm.maxEndDateForStart`), para
 * a tela e o 400 dizerem a mesma frase (SC-005).
 *
 * "Atualizar base" (FR-013) usa o período já calculado pelo servidor, mostra as
 * duas datas ANTES de confirmar e recusa quando `updateBase.available` é `false`,
 * com o `reason` do servidor como dica no botão (FR-014/FR-015).
 */
export const ImportForm = ({
  defaults,
  initialPeriod = null,
  busy,
  hasActiveRun = false,
  serverError,
  onSubmit,
  onUpdateBase,
}: Props) => {
  const { t } = useTranslation()
  const [values, setValues] = useState<ImportFormValues>(EMPTY)
  const [errors, setErrors] = useState<ImportFieldErrors>({})
  const [updateBaseOpen, setUpdateBaseOpen] = useState(false)
  const [updateBaseDryRun, setUpdateBaseDryRun] = useState(false)
  const [helpOpen, setHelpOpen] = useState(false)
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

  const maxRangeMonths = defaults?.maxRangeMonths ?? DEFAULT_MAX_RANGE_MONTHS

  // `hasCredentials` é `undefined` enquanto os defaults não chegam: nesse caso a
  // tela ainda não sabe se pode iniciar, e desenhar o aviso de "cadastre" seria
  // mentir antes da hora.
  const canStart = defaults === null || defaults.hasCredentials
  const needsUpdate = defaults?.credentialsStatus === 'needsUpdate'

  const updateBase = defaults?.updateBase ?? null
  const updateBaseAvailable = updateBase?.available === true
  const updateBaseReason = updateBase?.reason ?? null

  const update = (next: ImportFormValues): void => {
    setValues(next)

    // Depois da primeira tentativa a mensagem acompanha a digitação: some quando o
    // problema é resolvido, sem exigir novo envio.
    if (submittedOnce.current) {
      setErrors(validateImportForm(next, { maxRangeMonths }))
    }
  }

  const onText = (field: 'startDate' | 'endDate') => (event: ChangeEvent<HTMLInputElement>): void => {
    update({ ...values, [field]: event.target.value })
  }

  const onDryRun = (event: ChangeEvent<HTMLInputElement>): void => {
    update({ ...values, dryRun: event.target.checked })
  }

  const handleSubmit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault()
    submittedOnce.current = true

    const found = validateImportForm(values, { maxRangeMonths })

    setErrors(found)

    if (!isImportFormValid(found)) return

    onSubmit(values)
  }

  const confirmUpdateBase = (): void => {
    setUpdateBaseOpen(false)
    onUpdateBase(updateBaseDryRun)
  }

  /**
   * O bloqueio por recusas (FR-008a) mora em `lockedUntil`: enquanto houver, o
   * envio fica desabilitado e o horário de liberação — que só o servidor calcula no
   * fuso certo — aparece na tela. `formatReleaseTime` devolve `''` para `null` e
   * para instante ilegível, então "tem texto" é o próprio teste do bloqueio.
   */
  const releaseTime = formatReleaseTime(defaults?.lockedUntil ?? null)
  const locked = releaseTime !== ''
  const disabled = locked || hasActiveRun || busy

  const fieldProps = (field: keyof ImportFieldErrors) => ({
    'aria-invalid': errors[field] !== undefined,
    ...(errors[field] === undefined ? {} : { 'aria-describedby': errorIdOf(field) }),
  })

  const hint = (field: keyof ImportFieldErrors) => {
    const message = errors[field]

    return message === undefined ? null : (
      <p id={errorIdOf(field)} className="text-sm text-destructive">
        {t(message, { max: maxRangeMonths })}
      </p>
    )
  }

  /**
   * Link para a configuração (FR-009): o mesmo caminho da rota e do item de menu
   * (`navItems`), e o mesmo que o plugin devolve na recusa — um `<a>` do router,
   * não `navigate()`, para o endereço funcionar copiado ou aberto em aba nova.
   */
  const settingsLink = (
    <Button
      asChild
      variant="outline"
      className="min-h-11"
      data-testid="import-go-to-settings"
    >
      <Link to={MAIS_VENDAS_SETTINGS_PATH}>{t('import.goToSettings')}</Link>
    </Button>
  )

  /**
   * Por que o "Atualizar base" está indisponível (FR-014/FR-015): o `reason` vem do
   * servidor, na frase pronta — a tela não tem como saber se o motivo é "nenhum
   * pedido importado ainda" ou "a última importação foi há mais de 3 meses".
   *
   * Além da dica visual, o texto vai no `title` do invólucro (navegador não mostra
   * `title` de botão desabilitado, por isso o `<span>` envolve o botão) e no
   * `aria-describedby` do próprio botão: a informação tem de chegar a quem usa
   * leitor de tela, e não só a quem passa o mouse.
   */
  const updateBaseHintId = 'import-update-base-hint'

  /**
   * A dica é o `reason` do servidor, e só existe com o botão indisponível
   * (FR-014/FR-015): disponível, não há nada a explicar. `available: false` sem
   * `reason` (o contrato devolve os dois campos juntos, mas o servidor pode vir de
   * outra versão) deixa o botão mudo de propósito — a tela não inventa motivo.
   */
  const updateBaseHint = updateBaseAvailable ? null : updateBaseReason

  return (
    <Card className="gap-4">
      <CardHeader className="gap-1">
        <h2 className="text-lg font-semibold text-foreground">{t('import.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('import.subtitle')}</p>
      </CardHeader>

      <CardContent>
        {!canStart ? (
          /* Sem credenciais: o aviso entra no lugar do formulário e não há como
             iniciar (FR-009, contracts/ui.md). O botão de início continua desenhado
             e DESABILITADO para a vendedora ver o que está faltando, em vez de uma
             tela que simplesmente perdeu o botão. */
          <div className="flex flex-col gap-4" data-testid="import-no-credentials">
            <p
              role="alert"
              className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive"
            >
              {t('import.noCredentials')}
            </p>

            <div className="flex flex-wrap gap-2">
              {settingsLink}
              <Button
                type="button"
                className="min-h-11"
                disabled
                title={t('import.noCredentials')}
                data-testid="import-submit"
              >
                {t('import.start')}
              </Button>
            </div>
          </div>
        ) : (
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

            {needsUpdate ? (
              /* FR-007: credenciais recusadas pelo +Vendas. Avisa e oferece o
                 cadastro, mas NÃO bloqueia: a senha pode voltar a ser aceita na
                 próxima execução (research R5). */
              <div className="flex flex-col gap-2" data-testid="import-needs-update">
                <p className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive">
                  {t('import.needsUpdateHelp')}
                </p>
                <div className="flex flex-wrap gap-2">{settingsLink}</div>
              </div>
            ) : null}

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor={idOf('startDate')}>{t('import.startDate')}</Label>
                <Input
                  id={idOf('startDate')}
                  name="startDate"
                  className="min-h-11"
                  type="date"
                  data-testid="import-start-date"
                  // FR-011: não dá para ir além de hoje — o `max` nativo já corta o
                  // seletor, e a mesma regra continua no `validateImportForm` para
                  // quem digita ou cola a data.
                  max={todayInSaoPaulo()}
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
                  max={todayInSaoPaulo()}
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

            <div className="flex flex-wrap gap-2">
              <Button type="submit" className="min-h-11" disabled={disabled} data-testid="import-submit">
                {busy ? t('import.starting') : t('import.start')}
              </Button>

              {/*
                * "Atualizar base" (FR-013/FR-014/FR-015): habilitado só quando o
                * servidor calculou um período utilizável; sem isso o botão fica
                * desabilitado com o MOTIVO como dica — a vendedora lê por que não
                * pode, em vez de clicar num botão mudo. O `title` vai no invólucro
                * porque navegador não mostra `title` de `<button disabled>`; o mesmo
                * texto vai em `aria-describedby` e numa linha visível, para a
                * explicação não depender do mouse.
               */}
              <span
                className="inline-flex"
                {...(updateBaseHint === null ? {} : { title: updateBaseHint })}
                data-testid={updateBaseHint === null ? undefined : 'import-update-base-wrapper'}
              >
                <Button
                  type="button"
                  variant="outline"
                  className="min-h-11"
                  disabled={!updateBaseAvailable || disabled}
                  {...(updateBaseHint === null ? {} : { 'aria-describedby': updateBaseHintId })}
                  onClick={() => {
                    // A simulação do diálogo começa com o que a vendedora marcou no
                    // formulário: o "Atualizar base" é outra execução, não outro modo.
                    setUpdateBaseDryRun(values.dryRun)
                    setUpdateBaseOpen(true)
                  }}
                  data-testid="import-update-base"
                >
                  {t('import.updateBase')}
                </Button>
              </span>

              <Button
                type="button"
                variant="link"
                className="min-h-11"
                onClick={() => {
                  setHelpOpen(true)
                }}
                data-testid="import-update-base-help"
              >
                {t('import.updateBaseHelpTitle')}
              </Button>
            </div>

            {updateBaseHint !== null ? (
              <p id={updateBaseHintId} className="text-xs text-muted-foreground" data-testid="import-update-base-hint">
                {updateBaseHint}
              </p>
            ) : null}

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
        )}
      </CardContent>

      {/* Skill `react-modal`: o período do "Atualizar base" é mostrado ANTES de
          iniciar (FR-013), com a opção de simulação no próprio diálogo. */}
      <ConfirmModal
        open={updateBaseOpen}
        onOpenChange={setUpdateBaseOpen}
        onConfirm={confirmUpdateBase}
        title={t('import.updateBase')}
        description={t('import.updateBaseConfirm', {
          start: formatImportDate(updateBase?.startDate ?? null),
          end: formatImportDate(updateBase?.endDate ?? null),
        })}
        busy={busy}
        busyLabel={t('import.starting')}
        confirmLabel={t('import.start')}
        cancelLabel={t('import.cancelRun')}
        closeLabel={t('import.cancelRun')}
        testId="import-update-base-modal"
      >
        <p className="text-xs text-muted-foreground">{t('import.updateBaseConfirmHelp')}</p>

        <label className="flex min-h-11 items-start gap-3" htmlFor="import-update-base-dry-run">
          <input
            id="import-update-base-dry-run"
            type="checkbox"
            className="mt-1 size-5"
            checked={updateBaseDryRun}
            onChange={(event) => {
              setUpdateBaseDryRun(event.target.checked)
            }}
            data-testid="import-update-base-dry-run"
          />
          <span>
            <span className="block text-sm font-medium text-foreground">{t('import.dryRun')}</span>
            <span className="block text-xs text-muted-foreground">{t('import.dryRunHelp')}</span>
          </span>
        </label>
      </ConfirmModal>

      <Modal
        open={helpOpen}
        onOpenChange={setHelpOpen}
        title={t('import.updateBaseHelpTitle')}
        closeLabel={t('import.updateBaseHelpClose')}
        footer={
          <Button
            type="button"
            variant="outline"
            className="min-h-11"
            onClick={() => {
              setHelpOpen(false)
            }}
            data-testid="import-update-base-help-close"
          >
            {t('import.updateBaseHelpClose')}
          </Button>
        }
      >
        <p className="text-sm text-muted-foreground">{t('import.updateBaseHelpBody')}</p>
      </Modal>
    </Card>
  )
}

export default ImportForm
