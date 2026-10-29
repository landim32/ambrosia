/**
 * Import Types — importação do +Vendas (feature 006, contracts/imports-api.md) e
 * credenciais guardadas (feature 008, contracts/api.md).
 *
 * Espelho exato dos DTOs de `Ambrosio.DTO/Imports`: enums como as strings
 * camelCase que a API devolve (`JsonStringEnumConverter`), datas como
 * `AAAA-MM-DD` e instantes como ISO-8601 UTC.
 *
 * **Nenhum tipo de SAIDA aqui tem — ou pode ter — campo de senha, `login` de
 * execução ou `accountHash`** (FR-003/FR-006/SC-003): a garantia é estrutural.
 * A senha vive apenas no `useState` local do formulário de configuração enquanto
 * não é enviada, e o `GET`/`PUT`/`DELETE` de credenciais devolvem
 * `hasPassword: boolean` — nunca o valor (`MaisVendasCredentialsInfo` não tem
 * campo de senha, nem mascarado). O único `password` deste arquivo é o campo de
 * ENTRADA de `MaisVendasCredentialsSaveRequest`, que só transporta.
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

/**
 * Resposta de `GET /imports/mais-vendas/credentials` (feature 008, FR-001/FR-004)
 * e o corpo do `PUT` e do `DELETE` da mesma rota. Espelho de
 * `backend/Ambrosio.DTO/Imports/MaisVendasCredentialsInfo.cs`.
 *
 * **Não existe campo de senha aqui** (FR-003, SC-003): `hasPassword` diz se há
 * senha guardada sem dizer qual, e é por isso que a tela consegue afirmar "Senha
 * cadastrada em 28/10/2026" sem nunca ter recebido a senha. O `login` volta
 * inteiro porque a FR-004 permite exibi-lo à própria vendedora — ela quem o
 * cadastrou — e ele é o valor que o campo mostra para edição.
 */
export interface MaisVendasCredentials {
  /** Login guardado; `null` (ou vazio) quando nada foi cadastrado. */
  login: string | null
  /** Há senha cifrada guardada? Nunca o valor da senha (FR-003). */
  hasPassword: boolean
  /** Última gravação de login ou senha (instante UTC); `null` sem credenciais. */
  savedAt: string | null
  /** Última troca de senha (instante UTC) — a data que a tela exibe (data-model §1). */
  passwordChangedAt: string | null
  /**
   * Situação das credenciais (data-model §1): `none` = nada cadastrado,
   * `valid` = cadastradas e aceitas, `needsUpdate` = o +Vendas recusou a senha
   * (FR-007) — aviso na tela, nunca bloqueio de início.
   */
  status: 'none' | 'valid' | 'needsUpdate'
}

/**
 * Corpo do `PUT /imports/mais-vendas/credentials`: os dois campos seguem juntos e
 * `null` significa "não mexi nisso" — é o que permite trocar só a senha sem
 * reescrever o login, e o que faz a regra "trocar o login exige a senha" cair no
 * servidor com a mensagem dele (contracts/api.md), que a tela reutiliza sem
 * reescrever.
 *
 * O sufixo é o `Request` do frontend (como `ImportRunStartRequest`), e não o nome
 * do DTO de entrada da API: o que a tela tem de conhecer é o campo JSON.
 */
export interface MaisVendasCredentialsRequest {
  login: string | null
  password: string | null
}

/**
 * Rótulos i18n da situação das credenciais (tela de configuração e aviso da tela
 * de importação, FR-007). Igual aos mapas de `ImportRun['status']`: a tela
 * traduz por chave e nunca exibe o valor cru do enum.
 */
export const MAIS_VENDAS_CREDENTIALS_STATUS_KEYS = {
  none: 'settings.credentialsStatus.none',
  valid: 'settings.credentialsStatus.valid',
  needsUpdate: 'settings.credentialsStatus.needsUpdate',
} as const satisfies Record<MaisVendasCredentials['status'], string>

/** Fatia `updateBase` dos defaults (feature 008, FR-013/FR-014/FR-015). */
export interface ImportUpdateBaseDefaults {
  /** `false` sem pedido importado ou com intervalo maior que o limite (a tela desabilita). */
  available: boolean
  /** Data do último pedido importado do +Vendas (inclusive); `null` quando indisponível. */
  startDate: string | null
  /** Hoje em `America/Sao_Paulo`; `null` quando indisponível. */
  endDate: string | null
  /** Motivo da indisponibilidade, na frase do servidor — a tela usa como dica. */
  reason: string | null
}

/** Resposta de `GET /imports/mais-vendas/defaults` (FR-002/FR-008a + 008 R5). */
export interface ImportDefaults {
  /** Data do último pedido importado; `null` quando não há (tela: campo vazio e obrigatório). */
  suggestedStartDate: string | null
  /** Hoje em `America/Sao_Paulo` (research §11). */
  suggestedEndDate: string
  blockDays: number
  /**
   * Limite do período em MESES corridos (feature 008, research R6). Saiu
   * `maxRangeDays`: "3 meses" em dias variaria entre 89 e 92, e a conta por mês é
   * a que casa com a percepção da vendedora — `lib/importForm.ts` repete a conta
   * do servidor (`endDate <= startDate + maxRangeMonths - 1 dia`) para a mensagem
   * ser a mesma na tela e no 400 (SC-005).
   */
  maxRangeMonths: number
  /** Bloqueio por recusas seguidas de credencial; `null` quando livre. */
  lockedUntil: string | null
  /** Há login e senha guardados? Sem eles o início é recusado e a tela leva à configuração (FR-009). */
  hasCredentials: boolean
  /** Situação das credenciais; `needsUpdate` gera aviso e NUNCA bloqueio (FR-007, research R5). */
  credentialsStatus: MaisVendasCredentials['status']
  /** "Atualizar base" já calculado pelo servidor (FR-013): a tela não repete a regra. */
  updateBase: ImportUpdateBaseDefaults
}

/**
 * Corpo do `POST /imports/mais-vendas/runs` (feature 008, research R5): saem
 * `login` e `password` — o início passa a usar as credenciais GUARDADAS, e nenhum
 * campo de credencial existe aqui (FR-009). O serviço não retém nada: quem chama
 * monta o objeto com as datas e o descarta.
 */
export interface ImportRunStartRequest {
  startDate: string
  endDate: string
  dryRun: boolean
}

/** Corpo do `POST /imports/mais-vendas/runs/update-base` (FR-013): só o modo. */
export interface ImportUpdateBaseRequest {
  dryRun: boolean
}

/** Resumo da execução: o `202`, o item do histórico e a base do detalhe. */
export interface ImportRun {
  id: number
  /** Simulação ou gravação (FR-032). */
  mode: 'dryRun' | 'apply'
  /** Estado da execução; o resultado final segue a FR-047a. */
  status: 'queued' | 'running' | 'succeeded' | 'failed' | 'partial' | 'cancelled'
  /**
   * De onde o início veio (feature 008, research R8): `screen` é a tela e `mcp` é
   * a conversa do plugin. É O QUE DISTINGUE a execução do plugin na tela — ela
   * aparece aqui pelo polling de `runs/current`, sem a vendedora ter feito nada
   * (SC-002), e o rótulo "pelo plugin" evita que ela se assuste com um progresso
   * que não iniciou (contracts/ui.md).
   */
  startedVia: 'screen' | 'mcp'
  /** O que foi pedido: `period` (datas escolhidas) ou `updateBase` ("Atualizar base", FR-013). */
  kind: 'period' | 'updateBase'
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

/**
 * Rótulos de `startedVia` (feature 008, research R8). A tela só precisa dizer a
 * origem quando ela NÃO foi a vendedora: uma execução que aparece sozinha no
 * polling (`startedVia: 'mcp'`, SC-002) sem rótulo parece um bug — com ele, é o
 * plugin trabalhando (T027).
 */
export const IMPORT_STARTED_VIA_KEYS = {
  screen: 'import.via.screen',
  mcp: 'import.via.mcp',
} as const satisfies Record<ImportRun['startedVia'], string>

/** Rótulos de `kind` (FR-013): o "Atualizar base" tem de se diferenciar no histórico. */
export const IMPORT_RUN_KIND_KEYS = {
  period: 'import.kind.period',
  updateBase: 'import.kind.updateBase',
} as const satisfies Record<ImportRun['kind'], string>
