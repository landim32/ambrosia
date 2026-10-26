import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { CpfReveal } from '@/components/customers/CpfReveal'

/**
 * CpfReveal (FR-012, T047): mascarado por padrão, "mostrar" revela o completo e
 * vira "ocultar", e NADA e persistido — desmontar o componente volta a
 * mascarado (o Edge Case "CPF revelado").
 */
const MASKED = '***.456.789-**'
const FULL = '123.456.789-09'

const renderReveal = () => render(<CpfReveal cpf={FULL} cpfMasked={MASKED} />)

describe('CpfReveal', () => {
  it('começa mascarado com o botão "Mostrar"', () => {
    renderReveal()

    expect(screen.getByText(MASKED)).toBeInTheDocument()
    expect(screen.queryByText(FULL)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Mostrar/i })).toHaveAttribute('aria-pressed', 'false')
  })

  it('"Mostrar" revela o CPF completo e o botão vira "Ocultar"', async () => {
    const events = userEvent.setup()
    renderReveal()

    await events.click(screen.getByRole('button', { name: /Mostrar/i }))

    expect(screen.getByText(FULL)).toBeInTheDocument()
    expect(screen.queryByText(MASKED)).not.toBeInTheDocument()
    const toggle = screen.getByRole('button', { name: /Ocultar/i })
    expect(toggle).toHaveAttribute('aria-pressed', 'true')
  })

  it('"Ocultar" volta ao mascarado', async () => {
    const events = userEvent.setup()
    renderReveal()

    await events.click(screen.getByRole('button', { name: /Mostrar/i }))
    await events.click(screen.getByRole('button', { name: /Ocultar/i }))

    expect(screen.getByText(MASKED)).toBeInTheDocument()
    expect(screen.queryByText(FULL)).not.toBeInTheDocument()
  })

  /** O estado revelado nao e lembrado entre aberturas do perfil (FR-012). */
  it('remontar o componente volta a mascarado', async () => {
    const events = userEvent.setup()
    const first = render(<CpfReveal cpf={FULL} cpfMasked={MASKED} />)
    await events.click(first.getByRole('button', { name: /Mostrar/i }))
    expect(first.getByText(FULL)).toBeInTheDocument()
    first.unmount()

    renderReveal()

    expect(screen.getByText(MASKED)).toBeInTheDocument()
    expect(screen.queryByText(FULL)).not.toBeInTheDocument()
  })

  /** Sem `cpf` (API antiga / detalhe nao carregado): so o mascarado, sem botao. */
  it('sem CPF completo nao oferece o botão de revelar', () => {
    render(<CpfReveal cpfMasked={MASKED} />)

    expect(screen.getByText(MASKED)).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('não fabrica CPF a partir do mascarado', () => {
    render(<CpfReveal cpfMasked="123.456.789-09" />)

    // Um valor fora do formato da API vira placeholder neutro (FR-022).
    expect(screen.getByText('***.***.***-**')).toBeInTheDocument()
    expect(screen.queryByText('123.456.789-09')).not.toBeInTheDocument()
  })
})
