import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useRef, useState } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { OrderFiltersBar } from '@/components/orders/OrderFiltersBar'

import type { OrderFilters } from '@/types/order'

/**
 * Regressão de um bug em `/pedidos`: o debounce da barra
 * regravava na URL o filtro que o botão "Limpar filtros" (ou o botão voltar, ou
 * um endereço colado) acabara de apagar.
 *
 * A barra é controlada por fora, como na vida real (`useOrderFilters` → URL). O
 * `onChange` vive num ref para o `Harness` não capturar o mock de um teste
 * anterior: `beforeEach` troca o mock ANTES do `render`, mas o componente lê o
 * ref no momento da chamada.
 */
const state = {
  initial: { page: 1, pageSize: 20 } as OrderFilters,
  onChange: vi.fn(),
}

const Harness = () => {
  const [filters, setFilters] = useState<OrderFilters>(state.initial)
  const latest = useRef(state.onChange)
  latest.current = state.onChange

  return (
    <OrderFiltersBar
      filters={filters}
      onChange={(patch) => {
        latest.current(patch)
        setFilters((current) => ({ ...current, ...patch }))
      }}
      onClear={() => {
        setFilters({ page: 1, pageSize: 20 })
      }}
    />
  )
}

const settle = async (): Promise<void> => {
  await new Promise((resolve) => {
    setTimeout(resolve, 500)
  })
}

describe('OrderFiltersBar - origem da mudança (regressão)', () => {
  beforeEach(() => {
    state.initial = { page: 1, pageSize: 20 }
    state.onChange = vi.fn()
  })

  it('a digitação aplica o filtro depois da pausa', async () => {
    render(<Harness />)

    fireEvent.change(screen.getByLabelText('Canal'), { target: { value: 'inx' } })
    expect(state.onChange).not.toHaveBeenCalled()

    await waitFor(() => {
      expect(state.onChange).toHaveBeenCalled()
    })
    expect(state.onChange.mock.calls.at(-1)?.[0]).toMatchObject({ channel: 'inx', page: 1 })
  })

  /**
   * Com `canal=inx` na URL, o botão "Limpar filtros" zera o filtro. O valor
   * estável antigo ('inx') ainda chega pelo debounce ~400 ms depois; sem o gate
   * de origem ele viraria `onChange({ channel: 'inx' })` e re-escreveria a URL.
   */
  it('limpar por fora não é regravado pelo debounce (canal)', async () => {
    state.initial = { channel: 'inx', page: 1, pageSize: 20 }
    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByLabelText<HTMLInputElement>('Canal').value).toBe('inx')
    })

    fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }))
    await settle()

    expect(state.onChange).not.toHaveBeenCalled()
    expect(screen.getByLabelText<HTMLInputElement>('Canal').value).toBe('')
  })

  it('limpar por fora não é regravado pelo debounce (busca)', async () => {
    state.initial = { search: '4751', page: 1, pageSize: 20 }
    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByLabelText<HTMLInputElement>('Número do pedido ou cliente').value).toBe(
        '4751',
      )
    })

    fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }))
    await settle()

    expect(state.onChange).not.toHaveBeenCalled()
    expect(screen.getByLabelText<HTMLInputElement>('Número do pedido ou cliente').value).toBe('')
  })

  /** Digitar DEPOIS de limpar tem de funcionar — o gate não pode engessar o campo. */
  it('depois de limpar por fora, digitar volta a aplicar', async () => {
    state.initial = { channel: 'inx', page: 1, pageSize: 20 }
    render(<Harness />)

    fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }))
    await settle()
    expect(state.onChange).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText('Canal'), { target: { value: 'Site' } })
    await waitFor(() => {
      expect(state.onChange).toHaveBeenCalled()
    })
    expect(state.onChange.mock.calls.at(-1)?.[0]).toMatchObject({ channel: 'Site', page: 1 })
  })
})
