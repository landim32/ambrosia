import { describe, expect, it } from 'vitest'

import {
  countLabelKey,
  isImportFormValid,
  maxEndDateForStart,
  parseUtcInstant,
  periodFromParams,
  rangeDays,
  repeatBlockHref,
  todayInSaoPaulo,
  validateImportForm,
  type ImportFormValues,
} from '@/lib/importForm'

/**
 * Regras do formulário de importação (T038/T071 da 006; T021 da 008,
 * FR-003/FR-004/FR-010/FR-011/FR-026). "Hoje" é injetado: o teste não depende do
 * relógio da máquina nem do fuso do runner.
 *
 * O limite deixou de ser em dias (`maxRangeDays: 366`) e passou a em MESES
 * corridos, com a MESMA conta do `ImportRunStartInfoValidator`
 * (`endDate <= startDate.AddMonths(maxRangeMonths).AddDays(-1)`) — o que este
 * arquivo prova é que as duas pontas recusam o mesmo período (SC-005).
 */
const TODAY = '2026-10-27'
const LIMIT = { maxRangeMonths: 3 }

const valid = (overrides: Partial<ImportFormValues> = {}): ImportFormValues => ({
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

  it('não conhece mais nenhum campo de credencial (FR-009)', () => {
    // `login`/`password` saíram do formulário e do corpo do `POST`: não existe erro
    // de credencial a devolver — a recusa é do servidor e vira mensagem de tela.
    const errors = validateImportForm(valid(), LIMIT, TODAY)

    expect(Object.keys(errors)).toEqual([])
    expect('login' in errors).toBe(false)
    expect('password' in errors).toBe(false)
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

  it('recusa o período de 3 meses mais um dia (spec, Edge Cases)', () => {
    // 26/07 a 26/10 = 3 meses + 1 dia.
    const errors = validateImportForm(valid({ startDate: '2026-07-26', endDate: '2026-10-26' }), LIMIT, TODAY)

    expect(errors.endDate).toBe('validation.importRangeTooLong')
  })

  it('aceita o período de exatamente 3 meses', () => {
    // 26/07 a 25/10 é o último dia permitido por `AddMonths(3).AddDays(-1)`.
    const errors = validateImportForm(valid({ startDate: '2026-07-26', endDate: '2026-10-25' }), LIMIT, TODAY)

    expect(errors.endDate).toBeUndefined()
  })

  it('usa o número de meses dos defaults, não um valor fixo', () => {
    const twoMonths = { maxRangeMonths: 2 }

    // 26/07 + 2 meses - 1 dia = 25/09: 26/09 já passa, 25/09 não.
    expect(
      validateImportForm(valid({ startDate: '2026-07-26', endDate: '2026-09-25' }), twoMonths, TODAY).endDate,
    ).toBeUndefined()
    expect(
      validateImportForm(valid({ startDate: '2026-07-26', endDate: '2026-09-26' }), twoMonths, TODAY).endDate,
    ).toBe('validation.importRangeTooLong')
  })

  it('recusa um período que passa de 3 meses em dias, mas é menor que 92 dias', () => {
    // A conta é por mês: 01/01 a 31/03 são 90 dias e está DENTRO de 3 meses;
    // 31/01 a 30/04 são 90 dias e está FORA (o teto de 31/01 é 29/04).
    expect(
      validateImportForm(valid({ startDate: '2026-01-01', endDate: '2026-03-31' }), LIMIT, TODAY).endDate,
    ).toBeUndefined()
    expect(
      validateImportForm(valid({ startDate: '2026-01-31', endDate: '2026-04-30' }), LIMIT, TODAY).endDate,
    ).toBe('validation.importRangeTooLong')
  })
})

describe('maxEndDateForStart (a conta do servidor em JS)', () => {
  it('devolve o dia anterior à mesma data do terceiro mês seguinte', () => {
    expect(maxEndDateForStart('2026-07-26', 3)).toBe('2026-10-25')
    expect(maxEndDateForStart('2026-10-01', 3)).toBe('2026-12-31')
  })

  it('não deixa o dia estourar o mês, como DateOnly.AddMonths', () => {
    // 31/01 + 3 meses = 30/04 (abril não tem 31) → teto 29/04. Com `setMonth`
    // puro a conta rola para 01/05 e a tela aceitaria 30/04.
    expect(maxEndDateForStart('2026-01-31', 3)).toBe('2026-04-29')
    // Ano bissexto: fevereiro de 2028 tem 29 dias, então 31/01 + 1 mês é 29/02.
    expect(maxEndDateForStart('2028-01-31', 1)).toBe('2028-02-28')
    // 31/01 + 2 meses = 31/03 (março tem 31) → teto 30/03.
    expect(maxEndDateForStart('2028-01-31', 2)).toBe('2028-03-30')
  })

  it('cruza o ano', () => {
    expect(maxEndDateForStart('2026-11-15', 3)).toBe('2027-02-14')
  })

  it('devolve null em data ilegível (a regra de "informe a data" vem antes)', () => {
    expect(maxEndDateForStart('2026-13-40', 3)).toBeNull()
    expect(maxEndDateForStart('', 3)).toBeNull()
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
