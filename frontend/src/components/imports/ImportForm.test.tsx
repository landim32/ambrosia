import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ImportForm } from '@/components/imports/ImportForm'
import { defaultsFixture, storageDump } from '@/test/importFixtures'

import type { ImportDefaults } from '@/types/import'

/**
 * Formulário de importação (T039/T062/T079, FR-002/FR-003/FR-007/FR-008a/FR-032).
 * Só `Date` é falsificado: "hoje" fica em 27/10/2026 e o user-event continua com
 * os timers reais.
 */
const PASSWORD = 'SENHA-TESTE-XYZ123'

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-10-27T15:00:00Z'))
})

afterEach(() => {
  vi.useRealTimers()
})

const renderForm = (defaults: ImportDefaults | null = defaultsFixture(), busy = false) => {
  const onSubmit = vi.fn()
  const view = render(<ImportForm defaults={defaults} busy={busy} serverError={null} onSubmit={onSubmit} />)

  return { onSubmit, ...view }
}

const fillCredentials = async () => {
  const user = userEvent.setup()

  await user.type(screen.getByTestId('import-login'), 's482+ok')
  await user.type(screen.getByTestId('import-password'), PASSWORD)

  return user
}

describe('ImportForm', () => {
  it('preenche as datas sugeridas e deixa login/senha vazios', () => {
    renderForm()

    expect(screen.getByTestId('import-start-date')).toHaveValue('2026-09-26')
    expect(screen.getByTestId('import-end-date')).toHaveValue('2026-10-25')
    expect(screen.getByTestId('import-login')).toHaveValue('')
    expect(screen.getByTestId('import-password')).toHaveValue('')
  })

  it('deixa a data inicial vazia sem histórico de importação', () => {
    renderForm(defaultsFixture({ suggestedStartDate: null }))

    expect(screen.getByTestId('import-start-date')).toHaveValue('')
    expect(screen.getByText('Nenhum pedido importado ainda: informe a data inicial.')).toBeInTheDocument()
  })

  it('bloqueia o envio com a mensagem no campo', async () => {
    const { onSubmit } = renderForm(defaultsFixture({ suggestedStartDate: null }))
    const user = await fillCredentials()

    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Informe a data inicial do período.')).toBeInTheDocument()
    expect(screen.getByTestId('import-start-date')).toHaveAttribute('aria-invalid', 'true')
  })

  it('bloqueia período acima do limite citando o número de dias', async () => {
    const { onSubmit } = renderForm()
    const user = await fillCredentials()

    fireEvent.change(screen.getByTestId('import-start-date'), { target: { value: '2025-10-01' } })
    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('O período não pode ultrapassar 366 dias.')).toBeInTheDocument()
  })

  it('chama onSubmit com os valores válidos', async () => {
    const { onSubmit } = renderForm()
    const user = await fillCredentials()

    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).toHaveBeenCalledWith({
      login: 's482+ok',
      password: PASSWORD,
      startDate: '2026-09-26',
      endDate: '2026-10-25',
      dryRun: false,
    })
  })

  it('envia dryRun: true com "Simular (não gravar)" marcado', async () => {
    const { onSubmit } = renderForm()
    const user = await fillCredentials()

    await user.click(screen.getByTestId('import-dry-run'))
    await user.click(screen.getByTestId('import-submit'))

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ dryRun: true }))
  })

  it('usa autocomplete="off" no campo de senha', () => {
    renderForm()

    const password = screen.getByTestId('import-password')

    expect(password).toHaveAttribute('type', 'password')
    expect(password).toHaveAttribute('autocomplete', 'off')
  })

  it('limpa a senha assim que o envio começa e não a guarda em storage nem cookie', async () => {
    const { onSubmit, rerender } = renderForm()
    const user = await fillCredentials()

    await user.click(screen.getByTestId('import-submit'))
    expect(onSubmit).toHaveBeenCalled()

    rerender(<ImportForm defaults={defaultsFixture()} busy serverError={null} onSubmit={onSubmit} />)
    expect(screen.getByTestId('import-password')).toHaveValue('')

    // Fim do envio com erro: o campo continua vazio, a vendedora redigita.
    rerender(
      <ImportForm defaults={defaultsFixture()} busy={false} serverError="Erro qualquer." onSubmit={onSubmit} />,
    )
    expect(screen.getByTestId('import-password')).toHaveValue('')

    expect(storageDump(localStorage)).not.toContain(PASSWORD)
    expect(storageDump(sessionStorage)).not.toContain(PASSWORD)
    expect(document.cookie).not.toContain(PASSWORD)
  })

  it('desabilita o envio e mostra o horário de liberação no bloqueio', () => {
    // 18:30 UTC = 15:30 em São Paulo.
    renderForm(defaultsFixture({ lockedUntil: '2026-10-27T18:30:00' }))

    expect(screen.getByTestId('import-submit')).toBeDisabled()
    expect(screen.getByTestId('import-lock-message')).toHaveTextContent('15:30')
  })

  it('pré-preenche o período de "Repetir este bloco"', () => {
    render(
      <ImportForm
        defaults={defaultsFixture()}
        initialPeriod={{ start: '2026-10-10', end: '2026-10-16' }}
        busy={false}
        serverError={null}
        onSubmit={vi.fn()}
      />,
    )

    expect(screen.getByTestId('import-start-date')).toHaveValue('2026-10-10')
    expect(screen.getByTestId('import-end-date')).toHaveValue('2026-10-16')
    expect(screen.getByTestId('import-login')).toHaveValue('')
  })
})
