import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { ImportForm } from '@/components/imports/ImportForm'
import { defaultsFixture } from '@/test/importFixtures'

import type { ImportDefaults } from '@/types/import'

/**
 * "Atualizar base" no formulário (T025/T027 da 008, FR-013 a FR-015, US3-1; os
 * casos de T026 que moram na tela).
 *
 * Arquivo separado de `ImportForm.test.tsx` porque aqui há diálogo aberto com
 * user-event: o `Modal` move foco no `useEffect` de abertura e o user-event exige
 * timers REAIS para clicar nos botões do rodapé — os grupos que congelam `Date`
 * (para fixar o "hoje" do `max` das datas) não podem dividir o mesmo `beforeEach`.
 */
const REASON_NO_BASE = 'Ainda não há pedidos importados para atualizar.'
const REASON_TOO_LONG =
  'A última importação foi há mais de 3 meses. Importe em partes de até 3 meses, escolhendo as datas.'

const renderForm = (defaults: ImportDefaults, onUpdateBase = vi.fn()) => {
  const view = render(
    <MemoryRouter>
      <ImportForm
        defaults={defaults}
        busy={false}
        serverError={null}
        onSubmit={vi.fn()}
        onUpdateBase={onUpdateBase}
      />
    </MemoryRouter>,
  )

  return { onUpdateBase, ...view }
}

describe('ImportForm — Atualizar base', () => {
  it('habilita o botão quando o servidor calculou um período', () => {
    renderForm(defaultsFixture())

    expect(screen.getByTestId('import-update-base')).toBeEnabled()
    expect(screen.getByTestId('import-update-base')).toHaveTextContent('Atualizar base')
  })

  it('desabilita o botão sem pedido importado (FR-014)', () => {
    renderForm(
      defaultsFixture({
        updateBase: { available: false, startDate: null, endDate: null, reason: REASON_NO_BASE },
      }),
    )

    expect(screen.getByTestId('import-update-base')).toBeDisabled()
  })

  it('desabilita o botão quando o intervalo passa de 3 meses (FR-015)', () => {
    renderForm(
      defaultsFixture({
        updateBase: {
          available: false,
          startDate: '2026-01-10',
          endDate: '2026-10-27',
          reason: REASON_TOO_LONG,
        },
      }),
    )

    expect(screen.getByTestId('import-update-base')).toBeDisabled()
  })

  it('põe o motivo do servidor como dica do botão desabilitado (FR-014)', () => {
    renderForm(
      defaultsFixture({
        updateBase: { available: false, startDate: null, endDate: null, reason: REASON_NO_BASE },
      }),
    )

    // A dica mora num `<span>` por fora: navegador não mostra `title` de botão
    // desabilitado, e o `reason` é justamente a explicação que a vendedora precisa.
    expect(screen.getByTestId('import-update-base-wrapper')).toHaveAttribute('title', REASON_NO_BASE)
    expect(screen.getByTestId('import-update-base')).toHaveAccessibleDescription(REASON_NO_BASE)
    expect(screen.getByTestId('import-update-base-hint')).toHaveTextContent(REASON_NO_BASE)
  })

  it('não mostra dica quando o botão está disponível', () => {
    renderForm(defaultsFixture())

    expect(screen.queryByTestId('import-update-base-wrapper')).not.toBeInTheDocument()
    expect(screen.queryByTestId('import-update-base-hint')).not.toBeInTheDocument()
  })

  it('confirma mostrando o período calculado ANTES de iniciar (FR-013)', async () => {
    const user = userEvent.setup()
    const { onUpdateBase } = renderForm(defaultsFixture())

    await user.click(screen.getByTestId('import-update-base'))

    const dialog = screen.getByRole('dialog', { name: 'Atualizar base' })

    expect(dialog).toBeInTheDocument()
    // A pergunta de `contracts/ui.md`, com as duas datas do servidor em dd/mm/aaaa.
    expect(screen.getByText('Importar de 26/09/2026 a 25/10/2026?')).toBeInTheDocument()
    // Nada foi enviado ainda: o período é uma prévia, não um início.
    expect(onUpdateBase).not.toHaveBeenCalled()

    await user.click(screen.getByTestId('import-update-base-modal-confirm'))

    // Só o modo vai no corpo: o período é o do servidor (research R5).
    expect(onUpdateBase).toHaveBeenCalledWith(false)
  })

  it('inicia em simulação quando a opção é marcada no diálogo (FR-032)', async () => {
    const user = userEvent.setup()
    const { onUpdateBase } = renderForm(defaultsFixture())

    await user.click(screen.getByTestId('import-update-base'))
    await user.click(screen.getByTestId('import-update-base-dry-run'))
    await user.click(screen.getByTestId('import-update-base-modal-confirm'))

    expect(onUpdateBase).toHaveBeenCalledWith(true)
  })

  it('não inicia quando a confirmação é cancelada', async () => {
    const user = userEvent.setup()
    const { onUpdateBase } = renderForm(defaultsFixture())

    await user.click(screen.getByTestId('import-update-base'))
    await user.click(screen.getByTestId('import-update-base-modal-cancel'))

    expect(onUpdateBase).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('abre e fecha o "O que é atualizado?" (contracts/ui.md)', async () => {
    const user = userEvent.setup()

    renderForm(defaultsFixture())

    await user.click(screen.getByTestId('import-update-base-help'))

    const help = screen.getByRole('dialog', { name: 'O que é atualizado?' })

    expect(help).toBeInTheDocument()
    expect(
      screen.getByText(
        'A importação percorre o +Vendas do dia do último pedido já importado até hoje. O que já está na base volta como "inalterado"; entram os pedidos novos desse dia e os das datas seguintes. Nada é apagado.',
      ),
    ).toBeInTheDocument()

    // O "X", a sobreposição e o botão do rodapé têm o mesmo `aria-label` ("Fechar"),
    // então o clique é pelo identificador do botão do rodapé — o que a vendedora vê.
    await user.click(screen.getByTestId('import-update-base-help-close'))

    expect(screen.queryByRole('dialog', { name: 'O que é atualizado?' })).not.toBeInTheDocument()
  })

  it('desabilita o "Atualizar base" com execução em curso (FR-016)', () => {
    render(
      <MemoryRouter>
        <ImportForm
          defaults={defaultsFixture()}
          busy={false}
          hasActiveRun
          serverError={null}
          onSubmit={vi.fn()}
          onUpdateBase={vi.fn()}
        />
      </MemoryRouter>,
    )

    expect(screen.getByTestId('import-update-base')).toBeDisabled()
    expect(screen.getByTestId('import-submit')).toBeDisabled()
  })
})
