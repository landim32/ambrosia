import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { CustomerSummary } from '@/components/customers/CustomerSummary'

import type { CustomerSummaryInfo } from '@/types/customer'

/**
 * CustomerSummary (FR-010/FR-012, T047): compras, total líquido em R$, última
 * compra `dd/mm/aaaa` e dias desde a última compra. Cliente sem compras mostra
 * "0", `R$ 0,00` e "Sem compras" (quickstart C2.7).
 */
const nb = (value: string): string => value.replace(/\u00a0/g, ' ').replace(/\u202f/g, ' ')

describe('CustomerSummary', () => {
  it('mostra o resumo de um cliente com compras', () => {
    const summary: CustomerSummaryInfo = {
      purchases: 3,
      netTotal: 1234.56,
      lastPurchase: '2026-03-01',
      daysSinceLastPurchase: 12,
    }
    render(<CustomerSummary summary={summary} />)

    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText(nb('R$ 1.234,56'))).toBeInTheDocument()
    expect(screen.getByText('01/03/2026')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
  })

  it('cliente sem compras mostra 0, R$ 0,00 e "Sem compras"', () => {
    const summary: CustomerSummaryInfo = {
      purchases: 0,
      netTotal: 0,
      lastPurchase: null,
      daysSinceLastPurchase: null,
    }
    render(<CustomerSummary summary={summary} />)

    expect(screen.getByText('0')).toBeInTheDocument()
    expect(screen.getByText(nb('R$ 0,00'))).toBeInTheDocument()
    expect(screen.getByText('Sem compras')).toBeInTheDocument()
    // Sem última compra não há como contar dias: o traço de valor indisponível.
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  /** O total líquido pode ser negativo (devolução/cancelamento, FR-018). */
  it('exibe total negativo em reais', () => {
    const summary: CustomerSummaryInfo = {
      purchases: 1,
      netTotal: -450,
      lastPurchase: '2026-03-01',
      daysSinceLastPurchase: 5,
    }
    render(<CustomerSummary summary={summary} />)

    expect(screen.getByText(nb('-R$ 450,00'))).toBeInTheDocument()
  })

  it('cada campo tem rótulo visível (FR-029)', () => {
    const summary: CustomerSummaryInfo = {
      purchases: 2,
      netTotal: 10,
      lastPurchase: '2026-03-01',
      daysSinceLastPurchase: 3,
    }
    render(<CustomerSummary summary={summary} />)

    for (const label of [
      'Compras',
      'Total líquido',
      'Última compra',
      'Dias desde a última compra',
    ]) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
  })

  /** `daysSinceLastPurchase` nulo com compra existente (resumo parcial da API). */
  it('dias ausentes sai como traço, sem quebrar', () => {
    const summary: CustomerSummaryInfo = {
      purchases: 1,
      netTotal: 10,
      lastPurchase: '2026-03-01',
      daysSinceLastPurchase: null,
    }
    render(<CustomerSummary summary={summary} />)

    expect(screen.getByText('01/03/2026')).toBeInTheDocument()
    expect(screen.getAllByText('—')).toHaveLength(1)
  })

  it('data malformada não vaza texto cru (FR-020)', () => {
    const summary: CustomerSummaryInfo = {
      purchases: 1,
      netTotal: 10,
      // A API devolve dia-calendário; algo diferente é dado inesperado.
      lastPurchase: '2026-13-99',
      daysSinceLastPurchase: null,
    }
    render(<CustomerSummary summary={summary} />)

    expect(screen.queryByText('2026-13-99')).not.toBeInTheDocument()
    expect(screen.getByText('Sem compras')).toBeInTheDocument()
  })
})
