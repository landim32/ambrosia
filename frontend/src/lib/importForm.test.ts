import { describe, expect, it } from 'vitest'

import {
  countLabelKey,
  isImportFormValid,
  parseUtcInstant,
  periodFromParams,
  rangeDays,
  repeatBlockHref,
  todayInSaoPaulo,
  validateImportForm,
  type ImportFormValues,
} from '@/lib/importForm'

/**
 * Regras do formulário de importação (T038/T071, FR-003/FR-004/FR-026). "Hoje" é
 * injetado: o teste não depende do relógio da máquina nem do fuso do runner.
 */
const TODAY = '2026-10-27'
const LIMIT = { maxRangeDays: 366 }

const valid = (overrides: Partial<ImportFormValues> = {}): ImportFormValues => ({
  login: 's482+ok',
  password: 'v1',
  startDate: '2026-09-26',
  endDate: '2026-10-25',
  dryRun: false,
  ...overrides,
})

describe('validateImportForm', () => {
  it('aceita o período de referência', () => {
    const errors = validateImportForm(valid(), LIMIT, TODAY)

    expect(isImportFormValid(errors)).toBe(true)
  })

  it('exige a data inicial', () => {
    expect(validateImportForm(valid({ startDate: '' }), LIMIT, TODAY).startDate).toBe(
      'validation.importStartDateRequired',
    )
  })

  it('exige a data final', () => {
    expect(validateImportForm(valid({ endDate: '' }), LIMIT, TODAY).endDate).toBe(
      'validation.importEndDateRequired',
    )
  })

  it('bloqueia data inicial depois da final', () => {
    const errors = validateImportForm(valid({ startDate: '2026-10-26', endDate: '2026-10-25' }), LIMIT, TODAY)

    expect(errors.startDate).toBe('validation.importStartDateAfterEndDate')
    expect(isImportFormValid(errors)).toBe(false)
  })

  it('bloqueia data final no futuro e aceita hoje', () => {
    expect(validateImportForm(valid({ endDate: '2026-10-28' }), LIMIT, TODAY).endDate).toBe(
      'validation.importEndDateInFuture',
    )
    expect(validateImportForm(valid({ endDate: TODAY }), LIMIT, TODAY).endDate).toBeUndefined()
  })

  it('bloqueia período acima do limite, citando o campo final', () => {
    // 2025-10-26..2026-10-27 = 367 dias inclusivos.
    const errors = validateImportForm(valid({ startDate: '2025-10-26', endDate: TODAY }), LIMIT, TODAY)

    expect(errors.endDate).toBe('validation.importRangeTooLong')
  })

  it('aceita exatamente o limite', () => {
    // 2025-10-27..2026-10-27 = 366 dias inclusivos.
    const errors = validateImportForm(valid({ startDate: '2025-10-27', endDate: TODAY }), LIMIT, TODAY)

    expect(errors.endDate).toBeUndefined()
  })

  it('bloqueia login e senha vazios', () => {
    const errors = validateImportForm(valid({ login: '   ', password: '' }), LIMIT, TODAY)

    expect(errors.login).toBe('validation.importLoginRequired')
    expect(errors.password).toBe('validation.importPasswordRequired')
  })
})

describe('todayInSaoPaulo', () => {
  it('usa o fuso de São Paulo, não o UTC', () => {
    // 02:00 UTC de 28/10 ainda é 23:00 de 27/10 em São Paulo.
    expect(todayInSaoPaulo(new Date('2026-10-28T02:00:00Z'))).toBe('2026-10-27')
    expect(todayInSaoPaulo(new Date('2026-10-28T04:00:00Z'))).toBe('2026-10-28')
  })
})

describe('rangeDays', () => {
  it('conta dias inclusivos', () => {
    expect(rangeDays('2026-09-26', '2026-10-25')).toBe(30)
    expect(rangeDays('2026-10-25', '2026-10-25')).toBe(1)
  })
})

describe('periodFromParams (Repetir este bloco)', () => {
  it('lê inicio/fim válidos', () => {
    const params = new URLSearchParams('inicio=2026-10-10&fim=2026-10-16')

    expect(periodFromParams(params)).toEqual({ start: '2026-10-10', end: '2026-10-16' })
  })

  it('ignora datas inválidas, invertidas ou ausentes', () => {
    expect(periodFromParams(new URLSearchParams('inicio=2026-13-40&fim=2026-10-16'))).toBeNull()
    expect(periodFromParams(new URLSearchParams('inicio=2026-10-16&fim=2026-10-10'))).toBeNull()
    expect(periodFromParams(new URLSearchParams('inicio=2026-10-10'))).toBeNull()
  })

  it('monta o link de repetição sem credencial', () => {
    const href = repeatBlockHref({ start: '2026-10-10', end: '2026-10-16' })

    expect(href).toBe('/importacao?inicio=2026-10-10&fim=2026-10-16')
    expect(href).not.toMatch(/login|senha|password/i)
  })
})

describe('auxiliares do relatório', () => {
  it('lê instante sem fuso como UTC', () => {
    expect(parseUtcInstant('2026-10-27T18:30:00')?.toISOString()).toBe('2026-10-27T18:30:00.000Z')
    expect(parseUtcInstant('2026-10-27T18:30:00Z')?.toISOString()).toBe('2026-10-27T18:30:00.000Z')
    expect(parseUtcInstant(null)).toBeNull()
  })

  it('usa o condicional só nas contagens de simulação que o têm', () => {
    expect(countLabelKey('inserted', true)).toBe('import.wouldCounts.inserted')
    expect(countLabelKey('inserted', false)).toBe('import.counts.inserted')
    expect(countLabelKey('missing', true)).toBe('import.counts.missing')
  })
})
