import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { OrderFiltersBar } from '@/components/orders/OrderFiltersBar'

import type { OrderFilters } from '@/types/order'

/**
 * OrderFiltersBar (FR-014/FR-016, T065): todo campo tem rótulo acessível, trocar
 * o TIPO reposiciona a página 1, "Limpar filtros" zera tudo, e os campos de texto
 * saem com debounce (uma tecla não pode virar uma requisição).
 */
const renderBar = (overrides: Partial<OrderFilters> = {}) => {
  const onChange = vi.fn()
  const onClear = vi.fn()

  render(
    <OrderFiltersBar
      filters={{ page: 1, pageSize: 20, ...overrides }}
      onChange={onChange}
      onClear={onClear}
    />,
  )

  return { onChange, onClear }
}

const lastCall = (onChange: ReturnType<typeof vi.fn>): OrderFilters =>
  onChange.mock.calls.at(-1)?.[0] as OrderFilters

/**
 * Abre o `Select` do Radix pelo teclado: em jsdom o clique de ponteiro do
 * `userEvent` passa por `elementFromPoint` e o popover não abre de forma
 * confiável — e o teclado é exatamente o caminho que a FR-029 exige funcionar.
 */
const chooseKind = async (
  events: ReturnType<typeof userEvent.setup>,
  option: 'Todos' | 'Venda' | 'Devolução' | 'Cancelamento',
): Promise<void> => {
  const trigger = screen.getByRole('combobox')

  trigger.focus()
  await events.keyboard('{Enter}')
  await waitFor(() => {
    expect(screen.getByRole('listbox')).toBeInTheDocument()
  })
  await events.click(screen.getByRole('option', { name: option }))
}

describe('OrderFiltersBar', () => {
  it('todo campo tem rótulo acessível (FR-029)', () => {
    renderBar()

    for (const label of [
      'Data inicial',
      'Data final',
      'Tipo',
      'Canal',
      'Número do pedido ou cliente',
    ]) {
      expect(screen.getByText(label)).toBeInTheDocument()
      expect(screen.getByLabelText(label)).toBeInTheDocument()
    }
  })

  it('mostra os valores do filtro recebido', () => {
    renderBar({
      from: '2026-01-05',
      to: '2026-02-01',
      kind: 'return',
      channel: 'Site',
      search: '4751',
    })

    expect(screen.getByLabelText<HTMLInputElement>('Data inicial').value).toBe('2026-01-05')
    expect(screen.getByLabelText<HTMLInputElement>('Data final').value).toBe('2026-02-01')
    expect(screen.getByLabelText<HTMLInputElement>('Canal').value).toBe('Site')
    expect(screen.getByLabelText<HTMLInputElement>('Número do pedido ou cliente').value).toBe('4751')
    expect(screen.getByRole('combobox')).toHaveTextContent('Devolução')
  })

  it('alterar o tipo chama onChange com página 1', async () => {
    const { onChange } = renderBar({ page: 3 })
    const events = userEvent.setup()

    await chooseKind(events, 'Cancelamento')

    expect(onChange).toHaveBeenCalled()
    expect(lastCall(onChange).kind).toBe('cancellation')
    expect(lastCall(onChange).page).toBe(1)
  })

  it('o tipo "Todos" limpa o tipo', async () => {
    const { onChange } = renderBar({ kind: 'sale', page: 2 })
    const events = userEvent.setup()

    await chooseKind(events, 'Todos')

    expect(lastCall(onChange).kind).toBeUndefined()
    expect(lastCall(onChange).page).toBe(1)
  })

  it('Limpar filtros delega a limpeza', async () => {
    const { onClear } = renderBar({ kind: 'sale', channel: 'Linx', page: 4 })
    const events = userEvent.setup()

    await events.click(screen.getByRole('button', { name: 'Limpar filtros' }))

    expect(onClear).toHaveBeenCalledTimes(1)
  })

  it('canal é campo de texto livre (FR-016), com o limite da API', () => {
    renderBar()

    const channel = screen.getByLabelText<HTMLInputElement>('Canal')
    expect(channel).toHaveAttribute('type', 'text')
    expect(channel).toHaveAttribute('maxlength', '60')
  })

  it('busca tem o limite da API (60): 61+ caracteres seriam um 400', () => {
    renderBar()

    expect(
      screen.getByLabelText<HTMLInputElement>('Número do pedido ou cliente'),
    ).toHaveAttribute('maxlength', '60')
  })

  it('campos de período são type=date', () => {
    renderBar()

    expect(screen.getByLabelText<HTMLInputElement>('Data inicial').type).toBe('date')
    expect(screen.getByLabelText<HTMLInputElement>('Data final').type).toBe('date')
  })

  it('mudar data chama onChange com o valor digitado e página 1', () => {
    const { onChange } = renderBar({ page: 5 })

    fireEvent.change(screen.getByLabelText('Data inicial'), { target: { value: '2026-03-01' } })

    expect(onChange).toHaveBeenCalled()
    expect(lastCall(onChange).from).toBe('2026-03-01')
    expect(lastCall(onChange).page).toBe(1)
  })

  it('busca usa debounce: nada vai antes da pausa, e vai depois', async () => {
    const { onChange } = renderBar()

    fireEvent.change(screen.getByLabelText('Número do pedido ou cliente'), {
      target: { value: 'mar' },
    })

    expect(onChange).not.toHaveBeenCalled()

    await waitFor(() => {
      expect(onChange).toHaveBeenCalled()
    })
    expect(lastCall(onChange).search).toBe('mar')
  })

  it('canal também usa debounce', async () => {
    const { onChange } = renderBar()

    fireEvent.change(screen.getByLabelText('Canal'), { target: { value: 'lin' } })
    expect(onChange).not.toHaveBeenCalled()

    await waitFor(() => {
      expect(lastCall(onChange).channel).toBe('lin')
    })
  })

  it('não há seletor de "mês de cota" (FR-016)', () => {
    renderBar()

    expect(screen.queryByRole('combobox', { name: /mês/i })).not.toBeInTheDocument()
    expect(screen.queryByText(/mês de cota/i)).not.toBeInTheDocument()
  })
})
