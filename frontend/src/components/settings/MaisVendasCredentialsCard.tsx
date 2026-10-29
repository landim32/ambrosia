import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'

import { ConfirmModal } from '@/components/common/ConfirmModal'
import { LoadingList } from '@/components/common/LoadingList'
import { PasswordInput } from '@/components/common/PasswordInput'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { parseUtcInstant } from '@/lib/importForm'
import { useMaisVendasSettings } from '@/hooks/useMaisVendasSettings'

import type { ChangeEvent, FormEvent } from 'react'
import type { MaisVendasCredentials } from '@/types/import'
import { MAIS_VENDAS_CREDENTIALS_STATUS_KEYS } from '@/types/import'

/**
 * Mesmos tetos do `PUT /imports/mais-vendas/credentials` (contracts/api.md): a
 * tela corta antes de enviar COM A MESMA FRASE do servidor, e o servidor corta de
 * novo — regra dupla com textos idênticos é o que evita a vendedora ler "algo deu
 * errado" num campo que ela pode corrigir sozinha.
 */
const LOGIN_MAX_LENGTH = 120
const PASSWORD_MAX_LENGTH = 200

/** Erros por campo, com a chave i18n (nenhuma string montada aqui). */
interface CredentialsFieldErrors {
  login?: string
  password?: string
}

/**
 * `passwordChangedAt` (ou `savedAt`, na falta dele) como `dd/mm/aaaa` em São
 * Paulo — a frase "Senha cadastrada em dd/mm/aaaa" de `contracts/ui.md`.
 *
 * Lê com `parseUtcInstant` porque as colunas são `timestamp without time zone`
 * gravadas em UTC (constituição §V): sem o sufixo, `new Date()` interpretaria a
 * hora como LOCAL do navegador e a data mostrada poderia ser a véspera.
 */
const savedDateLabel = (credentials: MaisVendasCredentials | null): string => {
  const when = parseUtcInstant(credentials?.passwordChangedAt ?? credentials?.savedAt ?? null)
  if (when === null) return ''

  return new Intl.DateTimeFormat('pt-BR', {
    timeZone: 'America/Sao_Paulo',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(when)
}

/**
 * Cartão de credenciais do +Vendas da tela de configuração (feature 008, US1,
 * FR-001 a FR-007, `contracts/ui.md` "Configuração do +Vendas").
 *
 * O que NUNCA acontece aqui (FR-003, SC-003):
 * - a senha não volta do servidor em resposta alguma, então não há o que
 *   preencher: o campo abre VAZIO em toda montagem, com `type="password"` (dentro
 *   do `PasswordInput`) e `autocomplete="new-password"` — o gerenciador de senha do
 *   navegador tem nada a sugerir, porque nada ficou guardado no cliente;
 * - o valor digitado vive só neste `useState` local e é apagado no fim de qualquer
 *   desfecho do `PUT` — aceito, recusado ou erro de rede;
 * - nada é escrito em `localStorage`, `sessionStorage` ou cookie;
 * - não há "mostrar senha" do que está guardado: o Mostrar/Ocultar do
 *   `PasswordInput` vale só para o que a vendedora está digitando agora, que é o
 *   único caso que `contracts/ui.md` permite.
 *
 * A regra de troca de login (FR-005: "trocar o login DEVE exigir informar também a
 * senha") é a única comparação entre campos daqui; as demais recusas do `PUT`
 * (mesmo login com senha vazia = "nada muda") quem decide é o servidor, e a tela
 * mostra a mensagem dele sem reescrevê-la.
 */
export const MaisVendasCredentialsCard = () => {
  const { t } = useTranslation()
  const {
    credentials,
    loading,
    saving,
    removing,
    error,
    loadCredentials,
    saveCredentials,
    removeCredentials,
    clearError,
  } = useMaisVendasSettings()

  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<CredentialsFieldErrors>({})
  const [removeOpen, setRemoveOpen] = useState(false)

  /**
   * O login cadastrado preenche o campo UMA vez, quando a leitura chega — sem
   * amassar o que a vendedora já começou a digitar (o mesmo critério do
   * `datesFilled` do formulário de importação). A senha fica de fora daqui por
   * construção: não existe valor para semear (FR-003).
   */
  const [seeded, setSeeded] = useState(false)

  useEffect(() => {
    clearError()
    void loadCredentials()
  }, [clearError, loadCredentials])

  useEffect(() => {
    if (seeded || credentials === null) return

    setSeeded(true)
    setLogin(credentials.login ?? '')
  }, [credentials, seeded])

  // `status: 'none'` é o sinal de "nada cadastrado" (data-model §1); `hasPassword`
  // é o que libera a frase "Senha cadastrada em ..." — os dois andam juntos, mas a
  // tela pergunta cada um ao que ele diz.
  const hasCredentials = credentials !== null && credentials.status !== 'none'
  const savedLogin = credentials?.login ?? ''
  const needsUpdate = credentials?.status === 'needsUpdate'

  const validate = (nextLogin: string, nextPassword: string): CredentialsFieldErrors => {
    const found: CredentialsFieldErrors = {}

    if (nextLogin.trim() === '') found.login = 'validation.credentialsLoginRequired'
    else if (nextLogin.trim().length > LOGIN_MAX_LENGTH) found.login = 'validation.credentialsLoginTooLong'

    const changesLogin = hasCredentials && nextLogin.trim() !== savedLogin

    // Senha obrigatória em dois casos (contracts/ui.md): cadastrar do zero e trocar
    // de login. Com credenciais e o MESMO login, senha vazia significa "não mexi
    // nela" — o `PUT` devolve 200 sem nada mudar (contracts/api.md).
    if (nextPassword === '') {
      if (changesLogin) found.password = 'validation.credentialsPasswordForLoginChange'
      else if (!hasCredentials) found.password = 'validation.credentialsPasswordRequired'
    } else if (nextPassword.length > PASSWORD_MAX_LENGTH) {
      found.password = 'validation.credentialsPasswordTooLong'
    }

    return found
  }

  const onLoginChange = (event: ChangeEvent<HTMLInputElement>): void => {
    setLogin(event.target.value)
    clearError()

    // Depois da primeira tentativa a mensagem acompanha a digitação e some quando o
    // problema é resolvido, como no formulário de importação.
    if (errors.login !== undefined) setErrors(validate(event.target.value, password))
  }

  const onPasswordChange = (event: ChangeEvent<HTMLInputElement>): void => {
    setPassword(event.target.value)
    clearError()

    if (errors.password !== undefined) setErrors(validate(login, event.target.value))
  }

  const handleSubmit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault()

    const found = validate(login, password)

    setErrors(found)

    if (found.login !== undefined || found.password !== undefined) return

    void saveCredentials({
      login: login.trim(),
      // `null` = "mantém a senha guardada" (contracts/api.md). A senha em claro só
      // existe neste objeto e no corpo da requisição: ele não é guardado em lugar
      // nenhum e nem é devolvido por `saveCredentials`.
      password: password === '' ? null : password,
    }).then((ok) => {
      // A senha sai do estado em QUALQUER desfecho — o `PUT` já a levou. Num 400 a
      // vendedora redigita: não há motivo para deixar o valor na tela.
      setPassword('')

      if (!ok) return

      toast.success(t('settings.savedToast'))
    })
  }

  const handleRemove = (): void => {
    void removeCredentials().then((ok) => {
      if (!ok) return

      // O cadastro sumiu: o campo de login volta vazio junto (FR-006) e o cartão
      // reabre no estado "sem credenciais", que é o texto de cadastro inicial.
      setLogin('')
      setPassword('')
      setErrors({})
      setRemoveOpen(false)
      toast.success(t('settings.removedToast'))
    })
  }

  const idOf = (field: keyof CredentialsFieldErrors): string => `settings-${field}`
  const errorIdOf = (field: keyof CredentialsFieldErrors): string => `${idOf(field)}-error`

  const hint = (field: keyof CredentialsFieldErrors) => {
    const message = errors[field]

    return message === undefined ? null : (
      <p id={errorIdOf(field)} className="text-sm text-destructive">
        {t(message)}
      </p>
    )
  }

  const fieldProps = (field: keyof CredentialsFieldErrors) => ({
    'aria-invalid': errors[field] !== undefined,
    ...(errors[field] === undefined ? {} : { 'aria-describedby': errorIdOf(field) }),
  })

  const savedAt = savedDateLabel(credentials)

  return (
    <Card className="gap-4" data-testid="credentials-card">
      <CardHeader className="gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="grow text-lg font-semibold text-foreground">{t('settings.maisVendasSection')}</h2>
          {credentials !== null ? (
            <Badge
              variant={needsUpdate ? 'destructive' : hasCredentials ? 'secondary' : 'outline'}
              data-testid="credentials-status"
            >
              {t(MAIS_VENDAS_CREDENTIALS_STATUS_KEYS[credentials.status])}
            </Badge>
          ) : null}
        </div>

        {/* Estado sem credenciais (contracts/ui.md): a frase de orientação do zero. */}
        {credentials !== null && !hasCredentials ? (
          <p className="text-sm text-muted-foreground">{t('settings.emptyHelp')}</p>
        ) : null}
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        {credentials === null && loading ? <LoadingList count={2} /> : null}

        {/* FR-007: recusa do +Vendas — alerta, não bloqueio (research R5). */}
        {needsUpdate ? (
          <p
            role="alert"
            data-testid="credentials-needs-update"
            className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive"
          >
            {t('settings.credentialsNeedUpdate')}
          </p>
        ) : null}

        {error !== null ? (
          <p
            role="alert"
            data-testid="credentials-server-error"
            className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive"
          >
            {error}
          </p>
        ) : null}

        <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor={idOf('login')}>{t('settings.login')}</Label>
            <Input
              id={idOf('login')}
              name="maisVendasLogin"
              className="min-h-11"
              type="text"
              autoComplete="off"
              data-testid="settings-login"
              maxLength={LOGIN_MAX_LENGTH}
              value={login}
              onChange={onLoginChange}
              disabled={saving || removing}
              {...fieldProps('login')}
            />
            {hint('login')}
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor={idOf('password')}>{t('settings.password')}</Label>
            <PasswordInput
              id={idOf('password')}
              name="maisVendasPassword"
              value={password}
              onChange={onPasswordChange}
              // `new-password`, e não `off`: o navegador pode OFERECER gerar uma
              // senha nova, mas nunca preencher a guardada — a guardada não existe
              // no cliente (contracts/ui.md, FR-003).
              autoComplete="new-password"
              toggleLabels={{ show: t('common.showPassword'), hide: t('common.hidePassword') }}
              aria-invalid={errors.password !== undefined}
              {...(errors.password === undefined ? {} : { 'aria-describedby': errorIdOf('password') })}
            />

            {/* Abaixo da senha, a frase do contrato com `passwordChangedAt`. */}
            {credentials?.hasPassword ? (
              <p className="text-sm text-muted-foreground" data-testid="settings-password-saved">
                {savedAt === '' ? t('settings.passwordPresent') : t('settings.passwordSaved', { date: savedAt })}
              </p>
            ) : (
              <p className="text-sm text-muted-foreground">{t('settings.passwordAbsent')}</p>
            )}
            {hint('password')}
          </div>

          <div className="flex flex-wrap gap-2">
            <Button type="submit" className="min-h-11" disabled={saving || removing} data-testid="settings-save">
              {saving ? t('settings.saving') : t('settings.save')}
            </Button>

            {hasCredentials ? (
              <Button
                type="button"
                variant="destructive"
                className="min-h-11"
                disabled={saving || removing}
                onClick={() => {
                  setRemoveOpen(true)
                }}
                data-testid="settings-remove"
              >
                {removing ? t('settings.removing') : t('settings.remove')}
              </Button>
            ) : null}
          </div>
        </form>

        <p className="text-xs text-muted-foreground">{t('settings.securityNote')}</p>
      </CardContent>

      {/* Skill `react-modal`: remover credenciais pede confirmação em diálogo, nunca
          `window.confirm` (FR-006 apaga login e senha da base). */}
      <ConfirmModal
        open={removeOpen}
        onOpenChange={setRemoveOpen}
        onConfirm={handleRemove}
        title={t('settings.removeConfirmTitle')}
        description={t('settings.removeConfirmHelp')}
        confirmLabel={t('settings.removeConfirm')}
        cancelLabel={t('settings.cancel')}
        closeLabel={t('settings.cancel')}
        variant="danger"
        busy={removing}
        busyLabel={t('settings.removing')}
        testId="settings-remove-modal"
      />
    </Card>
  )
}

export default MaisVendasCredentialsCard
