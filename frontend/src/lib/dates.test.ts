import { afterEach, describe, expect, it } from 'vitest'

import { formatDateOnly, formatDateTime, isValidDateOnly, parseDateOnly } from '@/lib/dates'

/**
 * FR-020 / SC-007: datas `AAAA-MM-DD` são dia-calendário e NÃO podem mudar com
 * o fuso. Cada asserção roda nos três fusos de SC-007.
 */
const TIMEZONES = ['America/Sao_Paulo', 'UTC', 'Asia/Tokyo'] as const

const originalTz = process.env.TZ

const withTz = (tz: string, run: () => void): void => {
  process.env.TZ = tz
  try {
    run()
  } finally {
    if (originalTz === undefined) delete process.env.TZ
    else process.env.TZ = originalTz
  }
}

afterEach(() => {
  if (originalTz === undefined) delete process.env.TZ
  else process.env.TZ = originalTz
})

describe('parseDateOnly', () => {
  it.each(TIMEZONES)('em %s devolve o dia 1 de março de 2026 à meia-noite local', (tz) => {
    withTz(tz, () => {
      const date = parseDateOnly('2026-03-01')
      expect(date).not.toBeNull()
      expect(date?.getFullYear()).toBe(2026)
      expect(date?.getMonth()).toBe(2)
      expect(date?.getDate()).toBe(1)
      expect(date?.getHours()).toBe(0)
    })
  })

  it('não desloca o dia em fuso positivo (regressão do new Date(ISO))', () => {
    withTz('Asia/Tokyo', () => {
      // `new Date('2026-03-01')` é meia-noite em UTC → 10h em Tóquio, dia igual;
      // em fusos negativos (ex.: Antártica) viraria o dia anterior.
      expect(parseDateOnly('2026-03-01')?.getDate()).toBe(1)
    })
    withTz('America/Sao_Paulo', () => {
      expect(parseDateOnly('2026-03-01')?.getDate()).toBe(1)
    })
  })

  it('retorna null para texto inválido', () => {
    expect(parseDateOnly('2026-13-01')).toBeNull()
    expect(parseDateOnly('01/03/2026')).toBeNull()
    expect(parseDateOnly('')).toBeNull()
    expect(parseDateOnly(null)).toBeNull()
  })
})

describe('isValidDateOnly', () => {
  it.each(TIMEZONES)('aceita datas reais em %s', (tz) => {
    withTz(tz, () => {
      expect(isValidDateOnly('2026-03-01')).toBe(true)
      expect(isValidDateOnly('2024-02-29')).toBe(true)
      expect(isValidDateOnly('2000-12-31')).toBe(true)
    })
  })

  it('rejeita mês/dia impossíveis e formatos errados', () => {
    expect(isValidDateOnly('2026-13-99')).toBe(false)
    expect(isValidDateOnly('2026-02-30')).toBe(false)
    expect(isValidDateOnly('2025-02-29')).toBe(false)
    expect(isValidDateOnly('2026-3-1')).toBe(false)
    expect(isValidDateOnly('20260301')).toBe(false)
    expect(isValidDateOnly('2026-03-01T00:00:00Z')).toBe(false)
    expect(isValidDateOnly('')).toBe(false)
    expect(isValidDateOnly(undefined)).toBe(false)
  })
})

describe('formatDateOnly', () => {
  it.each(TIMEZONES)('%s → 01/03/2026', (tz) => {
    withTz(tz, () => {
      expect(formatDateOnly('2026-03-01')).toBe('01/03/2026')
    })
  })

  it.each(TIMEZONES)('%s → 31/12/2026 nas bordas do ano', (tz) => {
    withTz(tz, () => {
      expect(formatDateOnly('2026-12-31')).toBe('31/12/2026')
      expect(formatDateOnly('2026-01-01')).toBe('01/01/2026')
    })
  })

  it('devolve string vazia para entrada inválida ou nula', () => {
    expect(formatDateOnly('')).toBe('')
    expect(formatDateOnly(null)).toBe('')
    expect(formatDateOnly('lixo')).toBe('')
  })
})

describe('formatDateTime', () => {
  it('renderiza instante ISO em pt-BR com hora no fuso local', () => {
    withTz('America/Sao_Paulo', () => {
      expect(formatDateTime('2026-03-01T10:30:00Z')).toBe('01/03/2026 07:30')
    })
  })

  it('devolve string vazia para instante inválido', () => {
    expect(formatDateTime('não-é-data')).toBe('')
    expect(formatDateTime(null)).toBe('')
  })
})
