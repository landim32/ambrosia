import type {
  ImportDefaults,
  ImportRun,
  ImportRunBlock,
  ImportRunCounts,
  ImportRunDetail,
} from '@/types/import'

/**
 * Fixtures da tela de importação (feature 006). Valores do cenário bom do coletor
 * Fake (26/09–25/10/2026, cinco blocos de 7 dias). Nenhuma credencial aqui: os
 * testes que precisam de login/senha digitam placeholders na própria tela.
 */

/** Todo o conteúdo de um `Storage` como texto, para provar que a senha não foi parar lá (FR-007). */
export const storageDump = (storage: Storage): string => {
  const entries: string[] = []

  for (let index = 0; index < storage.length; index += 1) {
    const key = storage.key(index)

    if (key !== null) entries.push(`${key}=${storage.getItem(key) ?? ''}`)
  }

  return entries.join('\n')
}

export const ZERO_COUNTS: ImportRunCounts = {
  inserted: 0,
  updated: 0,
  unchanged: 0,
  missing: 0,
  failed: 0,
  conflicts: 0,
  customersCreated: 0,
  imagesStored: 0,
  imagesFailed: 0,
}

export const counts = (overrides: Partial<ImportRunCounts> = {}): ImportRunCounts => ({
  ...ZERO_COUNTS,
  ...overrides,
})

export const defaultsFixture = (overrides: Partial<ImportDefaults> = {}): ImportDefaults => ({
  suggestedStartDate: '2026-09-26',
  suggestedEndDate: '2026-10-25',
  blockDays: 7,
  maxRangeDays: 366,
  lockedUntil: null,
  ...overrides,
})

const BLOCK_PERIODS: [string, string][] = [
  ['2026-09-26', '2026-10-02'],
  ['2026-10-03', '2026-10-09'],
  ['2026-10-10', '2026-10-16'],
  ['2026-10-17', '2026-10-23'],
  ['2026-10-24', '2026-10-25'],
]

export const blockFixture = (blockNo: number, overrides: Partial<ImportRunBlock> = {}): ImportRunBlock => {
  const period = BLOCK_PERIODS[blockNo - 1] ?? ['2026-10-24', '2026-10-25']

  return {
    blockNo,
    startDate: period[0],
    endDate: period[1],
    status: 'succeeded',
    counts: counts(),
    errorMessage: null,
    startedAt: '2026-10-27T15:00:00',
    finishedAt: '2026-10-27T15:00:10',
    ...overrides,
  }
}

export const runFixture = (overrides: Partial<ImportRun> = {}): ImportRun => ({
  id: 42,
  mode: 'apply',
  status: 'succeeded',
  startDate: '2026-09-26',
  endDate: '2026-10-25',
  blockDays: 7,
  blocksTotal: 5,
  blocksFinished: 5,
  currentBlockNo: null,
  cancelRequested: false,
  requestedAt: '2026-10-27T15:00:00',
  startedAt: '2026-10-27T15:00:01',
  finishedAt: '2026-10-27T15:01:00',
  durationSeconds: 59,
  counts: counts({ inserted: 49, customersCreated: 12, imagesStored: 4 }),
  errorMessage: null,
  ...overrides,
})

export const detailFixture = (overrides: Partial<ImportRunDetail> = {}): ImportRunDetail => ({
  ...runFixture(),
  blocks: [1, 2, 3, 4, 5].map((n) => blockFixture(n)),
  changes: [],
  breakdown: [],
  previousRunOrdersCount: null,
  countDropWarning: false,
  ...overrides,
})
