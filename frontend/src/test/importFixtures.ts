import type {
  ImportDefaults,
  ImportRun,
  ImportRunBlock,
  ImportRunCounts,
  ImportRunDetail,
  ImportUpdateBaseDefaults,
  MaisVendasCredentials,
} from '@/types/import'

/**
 * Fixtures da tela de importação (feature 006) e da configuração do +Vendas
 * (feature 008). Valores do cenário bom do coletor Fake (26/09–25/10/2026, cinco
 * blocos de 7 dias).
 *
 * Nenhuma credencial aqui, em nenhum sentido: os DTOs de saída não têm campo de
 * senha (FR-003), então nem haveria valor para inventar — os testes de cadastro
 * digitam placeholders na própria tela e provam que o valor não fica em lugar
 * nenhum.
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

/** Fatia `updateBase` disponível (FR-013): 26/09 → hoje 25/10, três semanas de base. */
export const updateBaseAvailableFixture: ImportUpdateBaseDefaults = {
  available: true,
  startDate: '2026-09-26',
  endDate: '2026-10-25',
  reason: null,
}

export const defaultsFixture = (overrides: Partial<ImportDefaults> = {}): ImportDefaults => ({
  suggestedStartDate: '2026-09-26',
  suggestedEndDate: '2026-10-25',
  blockDays: 7,
  /** Feature 008: saiu `maxRangeDays`; o teto é em meses corridos, como no servidor (FR-010). */
  maxRangeMonths: 3,
  lockedUntil: null,
  /** Com credenciais por padrão: o formulário é o que se testa; T021 cobre o caso contrário. */
  hasCredentials: true,
  credentialsStatus: 'valid',
  updateBase: updateBaseAvailableFixture,
  ...overrides,
})

/** Defaults sem credenciais cadastradas: a tela de importação mostra o aviso (FR-009). */
export const defaultsWithoutCredentialsFixture = (
  overrides: Partial<ImportDefaults> = {},
): ImportDefaults =>
  defaultsFixture({
    hasCredentials: false,
    credentialsStatus: 'none',
    updateBase: { available: false, startDate: null, endDate: null, reason: null },
    ...overrides,
  })

/** Defaults com credenciais recusadas pelo +Vendas: aviso, mas o iniciar segue (FR-007). */
export const defaultsNeedingUpdateFixture = (overrides: Partial<ImportDefaults> = {}): ImportDefaults =>
  defaultsFixture({ credentialsStatus: 'needsUpdate', ...overrides })

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
  /** Feature 008 (research R8): o padrão é o que a tela faz — período escolhido, pela tela. */
  startedVia: 'screen',
  kind: 'period',
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

/** Execução iniciada pelo plugin do ChatGPT (SC-002): é `startedVia` que a distingue. */
export const runFromPluginFixture = (overrides: Partial<ImportRun> = {}): ImportRun =>
  runFixture({ startedVia: 'mcp', ...overrides })

/**
 * Resposta de `GET /imports/mais-vendas/credentials` sem cadastro (contracts/api.md:
 * `{ login: null, hasPassword: false, status: 'none' }`). **Não há campo de senha
 * para preencher aqui** — nem null, nem mascarado (FR-003).
 */
export const noCredentialsFixture = (overrides: Partial<MaisVendasCredentials> = {}): MaisVendasCredentials => ({
  login: null,
  hasPassword: false,
  savedAt: null,
  passwordChangedAt: null,
  status: 'none',
  ...overrides,
})

/** Credenciais válidas: o login volta (FR-004) e a senha não existe na resposta (FR-003). */
export const credentialsFixture = (overrides: Partial<MaisVendasCredentials> = {}): MaisVendasCredentials =>
  noCredentialsFixture({
    login: 's482+ok',
    hasPassword: true,
    savedAt: '2026-10-27T15:00:00',
    passwordChangedAt: '2026-10-27T15:00:00',
    status: 'valid',
    ...overrides,
  })

/** `needsUpdate`: o +Vendas recusou a senha guardada (FR-007). */
export const credentialsNeedingUpdateFixture = (
  overrides: Partial<MaisVendasCredentials> = {},
): MaisVendasCredentials => credentialsFixture({ status: 'needsUpdate', ...overrides })

export const detailFixture = (overrides: Partial<ImportRunDetail> = {}): ImportRunDetail => ({
  ...runFixture(),
  blocks: [1, 2, 3, 4, 5].map((n) => blockFixture(n)),
  changes: [],
  breakdown: [],
  previousRunOrdersCount: null,
  countDropWarning: false,
  ...overrides,
})
