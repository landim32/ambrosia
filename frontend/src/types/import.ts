/**
 * Import Types — importação do +Vendas (feature 006, contracts/imports-api.md).
 *
 * Espelho exato dos DTOs de `Ambrosia.DTO/Imports`: enums como as strings
 * camelCase que a API devolve (`JsonStringEnumConverter`), datas como
 * `AAAA-MM-DD` e instantes como ISO-8601 UTC.
 *
 * **Nenhum tipo aqui tem — ou pode ter — `login`, `password` ou `accountHash`**
 * (FR-006/SC-004): a garantia é estrutural, e a senha vive apenas no `useState`
 * local da página enquanto o formulário não é enviado.
 */

/*
 * Sem `type` (constituição §III/§IV, regra do projeto): as uniões ficam inline nas
 * interfaces e são referenciadas por acesso indexado — `ImportRun['status']`,
 * `ImportRunBlock['status']`, `ImportRunChange['action']` —, como `types/order.ts`
 * faz com `OrderInfo['kind']`.
 */

/** As nove contagens, de `import_runs` e de cada bloco (FR-047). */
export interface ImportRunCounts {
  inserted: number
  updated: number
  unchanged: number
  missing: number
  failed: number
  conflicts: number
  customersCreated: number
  imagesStored: number
  imagesFailed: number
}

/** Resposta de `GET /imports/mais-vendas/defaults` (FR-002/FR-008a). */
export interface ImportDefaults {
  /** Data do último pedido importado; `null` quando não há (tela: campo vazio e obrigatório). */
  suggestedStartDate: string | null
  /** Hoje em `America/Sao_Paulo` (research §11). */
  suggestedEndDate: string
  blockDays: number
  maxRangeDays: number
  /** Bloqueio por recusas seguidas de credencial; `null` quando livre. */
  lockedUntil: string | null
}

/** Corpo do `POST /imports/mais-vendas/runs` — o único lugar que carrega credencial. */
export interface ImportRunStartRequest {
  login: string
  password: string
  startDate: string
  endDate: string
  dryRun: boolean
}

/** Resumo da execução: o `202`, o item do histórico e a base do detalhe. */
export interface ImportRun {
  id: number
  /** Simulação ou gravação (FR-032). */
  mode: 'dryRun' | 'apply'
  /** Estado da execução; o resultado final segue a FR-047a. */
  status: 'queued' | 'running' | 'succeeded' | 'failed' | 'partial' | 'cancelled'
  startDate: string
  endDate: string
  blockDays: number
  blocksTotal: number
  blocksFinished: number
  currentBlockNo: number | null
  cancelRequested: boolean
  requestedAt: string
  startedAt: string | null
  finishedAt: string | null
  durationSeconds: number | null
  counts: ImportRunCounts
  /** Motivo em `failed`/`cancelled`; nunca contém credencial ou CPF (FR-052). */
  errorMessage: string | null
}

/** Fatia do período, com as contagens próprias (relatório por bloco). */
export interface ImportRunBlock {
  blockNo: number
  startDate: string
  endDate: string
  /** Estado do bloco (FR-009/FR-013). */
  status: 'pending' | 'running' | 'succeeded' | 'failed' | 'notExecuted'
  counts: ImportRunCounts
  errorMessage: string | null
  startedAt: string | null
  finishedAt: string | null
}

/** Uma linha do rastro: só a chave natural do pedido (FR-052). */
export interface ImportRunChange {
  /** `null` em simulação, em `error` e em `missing` sem pedido — o link só aparece com destino. */
  orderId: number | null
  orderNumber: string
  orderDate: string
  kind: 'sale' | 'return' | 'cancellation'
  channel: string | null
  origin: 'cota' | 'omni' | null
  /** O que a importação decidiu para o pedido (FR-048). */
  action: 'inserted' | 'updated' | 'unchanged' | 'missing' | 'conflict' | 'error'
  changedFields: string[] | null
  errorMessage: string | null
}

/** Agrupamento canal × origem × tipo × ação do relatório (FR-049). */
export interface ImportRunBreakdown {
  channel: string | null
  origin: ImportRunChange['origin']
  kind: ImportRunChange['kind']
  action: ImportRunChange['action']
  count: number
}

/** Relatório completo (`runs/current`, `runs/{id}` e a resposta do cancelamento). */
export interface ImportRunDetail extends ImportRun {
  blocks: ImportRunBlock[]
  changes: ImportRunChange[]
  breakdown: ImportRunBreakdown[]
  /** Pedidos vistos pela última execução concluída/parcial do mesmo período (FR-051). */
  previousRunOrdersCount: number | null
  countDropWarning: boolean
}

/** Parâmetros de `GET /imports/mais-vendas/runs`. */
export interface ImportRunListQuery {
  page: number
  pageSize: number
}

/** Rótulos i18n por status/bloco/ação: a tela traduz por chave, nunca exibe o valor cru. */
export const IMPORT_STATUS_KEYS = {
  queued: 'import.status.queued',
  running: 'import.status.running',
  succeeded: 'import.status.succeeded',
  failed: 'import.status.failed',
  partial: 'import.status.partial',
  cancelled: 'import.status.cancelled',
} as const satisfies Record<ImportRun['status'], string>

export const IMPORT_BLOCK_STATUS_KEYS = {
  pending: 'import.blockStatus.pending',
  running: 'import.blockStatus.running',
  succeeded: 'import.blockStatus.succeeded',
  failed: 'import.blockStatus.failed',
  notExecuted: 'import.blockStatus.notExecuted',
} as const satisfies Record<ImportRunBlock['status'], string>

export const IMPORT_ACTION_KEYS = {
  inserted: 'import.action.inserted',
  updated: 'import.action.updated',
  unchanged: 'import.action.unchanged',
  missing: 'import.action.missing',
  conflict: 'import.action.conflict',
  error: 'import.action.error',
} as const satisfies Record<ImportRunChange['action'], string>

/** A execução ainda vai andar: o polling continua (FR-015). */
export const isImportRunActive = (status: ImportRun['status']): boolean =>
  status === 'queued' || status === 'running'
