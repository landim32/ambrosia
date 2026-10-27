import { afterEach, describe, expect, it } from 'vitest'

import { formatCurrency } from '@/lib/currency'

const originalTz = process.env.TZ

afterEach(() => {
  if (originalTz === undefined) delete process.env.TZ
  else process.env.TZ = originalTz
})

/** O `Intl` separa moeda com NBSP (U+00A0) ou NNBSP (U+202F) dependendo do ICU. */
const normalize = (value: string): string => value.replace(/[\u00A0\u202F]/g, ' ')

describe('formatCurrency (FR-019)', () => {
  it('formata positivo em R$ com milhar e duas casas', () => {
    expect(normalize(formatCurrency(1234.56))).toBe('R$ 1.234,56')
  })

  it('sinal antes do R$ em negativo', () => {
    expect(normalize(formatCurrency(-1234.56))).toBe('-R$ 1.234,56')
  })

  it('zero sai como R$ 0,00', () => {
    expect(normalize(formatCurrency(0))).toBe('R$ 0,00')
  })

  it('arredonda para centavos', () => {
    expect(normalize(formatCurrency(10.005))).toBe('R$ 10,01')
    expect(normalize(formatCurrency(99.9))).toBe('R$ 99,90')
  })

  it('estável nos três fusos de SC-007', () => {
    for (const tz of ['America/Sao_Paulo', 'UTC', 'Asia/Tokyo']) {
      process.env.TZ = tz
      expect(normalize(formatCurrency(1234.56))).toBe('R$ 1.234,56')
      expect(normalize(formatCurrency(-1234.56))).toBe('-R$ 1.234,56')
    }
  })

  it('devolve R$ 0,00 para valores não numéricos', () => {
    expect(normalize(formatCurrency(Number.NaN))).toBe('R$ 0,00')
  })
})
