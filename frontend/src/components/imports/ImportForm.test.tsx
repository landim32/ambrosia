import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ImportForm } from '@/components/imports/ImportForm'
import {
  defaultsFixture,
  defaultsNeedingUpdateFixture,
  defaultsWithoutCredentialsFixture,
  storageDump,
} from '@/test/importFixtures'

import type { ImportDefaults } from '@/types/import'

/**
 * Formulário de importação (T039/T062/T079 da 006; T021 da 008,
 * FR-002/FR-003/FR-009/FR-010/FR-008a/FR-032).
 *
 * Só `Date` é falsificado: "hoje" fica em 27/10/2026 e o user-event continua com
 * os timers reais.
 *
 * A feature 008 tirou daqui os dois campos de credencial, então a prova principal
 * mudou de lado: antes era "a senha não fica retida"; agora é "não existe campo de
 * senha para reter" (FR-009) — os testes procuram o campo e afirmam a ausência.
 */
const SETTINGS_PATH = '/configuracoes/mais-vendas'

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-10-27T15:00:00Z'))
})

afterEach(() => {
  vi.useRealTimers()
})

const renderForm = (
  defaults: ImportDefaults | null = defaultsFixture(),
  busy = false,
  onSubmit = vi.fn(),
) => {
  const view = render(
    <MemoryRouter>
      <ImportForm
        defaults={defaults}
        busy={busy}
        serverError={null}
        onSubmit={onSubmit}
        onUpdateBase={vi.fn()}
      />
    </MemoryRouter>,
  )

  return { onSubmit, ...view }
}

/**
 * Preenche o período com `fireEvent.change` (o `input type="date"` não aceita
 * digitação por tecla no jsdom) e devolve o user-event para o clique — função
 * SINCRÔNICA de propósito: não há `await` aqui, e `require-await` não deixa mentir
 * para os chamadores com um `async` que não espera nada.
 */
const fillPeriod = (start = '2026-09-26', end = '2026-10-25') => {
  const user = userEvent.setup()

  fireEvent.change(screen.getByTestId('import-start-date'), { target: { value: start } })
  fireEvent.change(screen.getByTestId('import-end-date'), { target: { value: end } })

  return user
}

describe('ImportForm', () => {
  it('não tem campo de login nem de senha (FR-009)', () => {
    renderForm()

    expect(screen.queryByTestId('import-login')).not.toBeInTheDocument()
    expect(screen.queryByTestId('import-password')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Login do +Vendas')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Senha do +Vendas')).not.toBeInTheDocument()

    // E o DOM inteiro não tem input de senha: não há onde a vendedora digitar uma.
    expect(document.querySelectorAll('input[type="password"]')).toHaveLength(0)
  })

  it('preenche as datas sugeridas', () => {
    renderForm()

    expect(screen.getByTestId('import-start-date')).toHaveValue('2026-09-26')
    expect(screen.getByTestId('import-end-date')).toHaveValue('2026-10-25')
  })

  it('deixa a data inicial vazia sem histórico de importação', () => {
    renderForm(defaultsFixture({ suggestedStartDate: null }))

    expect(screen.getByTestId('import-start-date')).toHaveValue('')
    expect(screen.getByText('Nenhum pedido importado ainda: informe a data inicial.')).toBeInTheDocument()
  })

  it('bloqueia o envio com a mensagem no campo', async () => {
    const { onSubmit } = renderForm(defaultsFixture({ suggestedStartDate: null }))
    const user = userEvent.setup()

    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Informe a data inicial do período.')).toBeInTheDocument()
    expect(screen.getByTestId('import-start-date')).toHaveAttribute('aria-invalid', 'true')
  })

  it('bloqueia período acima de 3 meses citando o número configurado', async () => {
    const { onSubmit } = renderForm()
    const user = fillPeriod('2026-07-26', '2026-10-26')

    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('O período não pode passar de 3 meses.')).toBeInTheDocument()
  })

  it('aceita o período de exatamente 3 meses', async () => {
    const { onSubmit } = renderForm()
    const user = fillPeriod('2026-07-26', '2026-10-25')

    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).toHaveBeenCalledWith({ startDate: '2026-07-26', endDate: '2026-10-25', dryRun: false })
  })

  it('mostra o número de meses dos defaults quando o limite é outro', async () => {
    const { onSubmit } = renderForm(defaultsFixture({ maxRangeMonths: 2 }))
    const user = fillPeriod('2026-07-26', '2026-09-26')

    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('O período não pode passar de 2 meses.')).toBeInTheDocument()
  })

  it('limita a data final a hoje no seletor (FR-011)', () => {
    renderForm()

    expect(screen.getByTestId('import-end-date')).toHaveAttribute('max', '2026-10-27')
    expect(screen.getByTestId('import-start-date')).toHaveAttribute('max', '2026-10-27')
  })

  it('chama onSubmit com os valores válidos', async () => {
    const { onSubmit } = renderForm()
    const user = userEvent.setup()

    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).toHaveBeenCalledWith({ startDate: '2026-09-26', endDate: '2026-10-25', dryRun: false })
  })

  it('envia dryRun: true com "Simular (não gravar)" marcado', async () => {
    const { onSubmit } = renderForm()
    const user = userEvent.setup()

    await user.click(screen.getByTestId('import-dry-run'))
    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ dryRun: true }))
  })

  it('não escreve nada de credencial em storage nem cookie', () => {
    renderForm()

    // Sem campo de senha o vazamento deixou de ser possível; a asserção continua
    // aqui para pegar qualquer regressão que volte a guardar algo do +Vendas no
    // cliente (FR-003, SC-003).
    expect(storageDump(localStorage)).toBe('')
    expect(storageDump(sessionStorage)).toBe('')
    expect(document.cookie).toBe('')
  })

  it('sem credenciais troca o formulário pelo aviso com o link da configuração e o iniciar desabilitado', () => {
    renderForm(defaultsWithoutCredentialsFixture())

    expect(screen.getByTestId('import-no-credentials')).toBeInTheDocument()
    expect(screen.getByText('Cadastre as credenciais do +Vendas para importar.')).toBeInTheDocument()
    expect(screen.getByTestId('import-go-to-settings')).toHaveAttribute('href', SETTINGS_PATH)

    // Nada do período à vista e o início indisponível (FR-009).
    expect(screen.queryByTestId('import-start-date')).not.toBeInTheDocument()
    expect(screen.getByTestId('import-submit')).toBeDisabled()
  })

  it('com needsUpdate mostra o aviso com o link e mantém o iniciar disponível (FR-007)', async () => {
    const { onSubmit } = renderForm(defaultsNeedingUpdateFixture())

    expect(screen.getByTestId('import-needs-update')).toBeInTheDocument()
    expect(screen.getByText('O +Vendas recusou a senha cadastrada. Digite a senha atual para continuar importando.'))
      .toBeInTheDocument()
    expect(screen.getByTestId('import-go-to-settings')).toHaveAttribute('href', SETTINGS_PATH)
    expect(screen.getByTestId('import-start-date')).toBeInTheDocument()
    expect(screen.getByTestId('import-submit')).toBeEnabled()

    const user = userEvent.setup()

    await user.click(screen.getByTestId('import-submit'))
    expect(onSubmit).toHaveBeenCalled()
  })

  it('desabilita o envio e mostra o horário de liberação no bloqueio', () => {
    // 18:30 UTC = 15:30 em São Paulo.
    renderForm(defaultsFixture({ lockedUntil: '2026-10-27T18:30:00' }))

    expect(screen.getByTestId('import-submit')).toBeDisabled()
    expect(screen.getByTestId('import-lock-message')).toHaveTextContent('15:30')
  })

  it('pré-preenche o período de "Repetir este bloco"', () => {
    render(
      <MemoryRouter>
        <ImportForm
          defaults={defaultsFixture()}
          initialPeriod={{ start: '2026-10-10', end: '2026-10-16' }}
          busy={false}
          serverError={null}
          onSubmit={vi.fn()}
          onUpdateBase={vi.fn()}
        />
      </MemoryRouter>,
    )

    expect(screen.getByTestId('import-start-date')).toHaveValue('2026-10-10')
    expect(screen.getByTestId('import-end-date')).toHaveValue('2026-10-16')
  })
})
