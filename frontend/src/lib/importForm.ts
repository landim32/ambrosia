import { formatDateOnly, isValidDateOnly, parseDateOnly } from '@/lib/dates'

import type { ImportDefaults, ImportRunCounts } from '@/types/import'

/**
 * Regras de formulário da tela de importação (feature 006, FR-003/FR-004/FR-006).
 *
 * Espelho local do `ImportRunStartInfoValidator` da API: a tela bloqueia o envio
 * com a mensagem no campo, e a mesma requisição enviada direto ao servidor cai nas
 * mesmas regras (spec US1-5). Nenhuma mensagem repete o valor digitado (FR-006).
 *
 * "Hoje" é sempre o de `America/Sao_Paulo` (research §11) — o fuso é o do negócio,
 * e UTC às 20h já seria amanhã e a data de hoje pareceria futuro.
 */

/** Intervalo do polling de progresso, em ms (FR-015; ≤ 5 s de atraso com 3 s). */
export const IMPORT_POLL_MS = 3000

/** Chaves de `inicio`/`fim` na query string ("Repetir este bloco", FR-026). */
export const IMPORT_QUERY_START_KEY = 'inicio'
export const IMPORT_QUERY_END_KEY = 'fim'

/** Erros por campo, com a chave i18n (nenhuma string montada aqui). */
export interface ImportFieldErrors {
  startDate?: string
  endDate?: string
}

/**
 * Valores brutos do formulário (nenhum corte antes da validação).
 *
 * Feature 008: saem `login` e `password`. A tela não tem mais campo de credencial
 * — o início usa as credenciais GUARDADAS no servidor (FR-009) —, então nem
 * existe valor de senha para passar por aqui.
 */
export interface ImportFormValues {
  startDate: string
  endDate: string
  dryRun: boolean
}

/** O período de referência da tela, já em dia-calendário. */
export interface ImportPeriod {
  start: string
  end: string
}

/**
 * Hoje em São Paulo como `AAAA-MM-DD`.
 *
 * Usa `Intl` com o fuso explícito em vez de `Date` local: o navegador da vendedora
 * pode estar em outro fuso (e nos testes roda em UTC), e a data limite do período é
 * a de Brasília, não a da máquina.
 */
export const todayInSaoPaulo = (now: Date = new Date()): string =>
  new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Sao_Paulo',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now)

/** Dias do mês em ano não bissexto (índice 0 = janeiro). */
const MONTH_DAYS = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]

const isLeapYear = (year: number): boolean => (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0

/** `year`/`month` com `month` de 0 a 11, como `Date.getMonth()`. */
const daysInMonth = (year: number, month: number): number =>
  month === 1 && isLeapYear(year) ? 29 : MONTH_DAYS[month] ?? 31

const pad2 = (value: number): string => String(value).padStart(2, '0')

/**
 * Soma meses a um dia-calendário SEM DEIXAR O DIA ESTOURAR o mês destino, que é
 * exatamente o que `DateOnly.AddMonths` do .NET faz (`AddMonths` de 30/01 + 1 mês
 * é 28/02 ou 29/02, nunca 02/03).
 *
 * `Date` puro não serve: `new Date(2026, 0, 31).setMonth(2)` rola para 03/03 e a
 * tela aceitaria um período que o servidor recusa — e a regra tem de ser A MESMA
 * nos dois (research R6, SC-005). Trabalha com componentes (`getFullYear` etc.) e
 * nunca com instante UTC, porque o valor é dia-calendário (FR-020).
 */
const addMonthsClamped = (date: Date, months: number): Date => {
  const index = date.getFullYear() * 12 + date.getMonth() + months
  const year = Math.floor(index / 12)
  const month = index % 12
  const day = Math.min(date.getDate(), daysInMonth(year, month))

  return new Date(year, month, day)
}

/**
 * Última data final válida para a data inicial dada (FR-010): o dia anterior à
 * mesma data do `maxRangeMonths`-ésimo mês seguinte — `startDate.AddMonths(n).AddDays(-1)`
 * do validator da API, em JS. 26/07 com 3 meses devolve 25/10: 26/07–25/10 entra e
 * 26/07–26/10 é recusado (spec, Edge Cases).
 *
 * `null` quando a data inicial não é um dia-calendário real: quem chama já tem a
 * regra de "informe a data inicial" antes desta e não deve somar em cima de lixo.
 */
export const maxEndDateForStart = (startDate: string, maxRangeMonths: number): string | null => {
  const from = parseDateOnly(startDate)
  if (from === null) return null

  const lastAllowed = addMonthsClamped(from, maxRangeMonths)

  // `AddDays(-1)` em dia-calendário: `setDate(0)` cai no último dia do mês anterior,
  // então um dia a menos é sempre possível sem conta de empréstimo entre meses.
  lastAllowed.setDate(lastAllowed.getDate() - 1)

  return `${String(lastAllowed.getFullYear())}-${pad2(lastAllowed.getMonth() + 1)}-${pad2(
    lastAllowed.getDate(),
  )}`
}

/** Dias inclusivos entre duas datas `AAAA-MM-DD` (negativo quando invertidas). */
export const rangeDays = (start: string, end: string): number => {
  const from = parseDateOnly(start)
  const to = parseDateOnly(end)

  if (from === null || to === null) return 0

  const MS_PER_DAY = 86_400_000

  return Math.round((to.getTime() - from.getTime()) / MS_PER_DAY) + 1
}

/**
 * Validação do formulário (FR-003/FR-004/FR-010/FR-011). `today` é injetável para
 * os testes terem um "hoje" fixo; o padrão é o relógio de São Paulo.
 *
 * Ordem deliberada: campo obrigatório antes de comparação entre campos — um
 * período sem data final não deve gerar, além de "informe a data final", um
 * "a data inicial deve ser anterior" sem sentido.
 *
 * Nenhuma regra de credencial existe aqui desde a feature 008: sem credenciais
 * cadastradas o formulário nem é desenhado (contracts/ui.md), e a recusa do
 * servidor ("Cadastre as credenciais do +Vendas na configuração.") é mensagem de
 * tela, não de campo.
 */
export const validateImportForm = (
  values: ImportFormValues,
  defaults: Pick<ImportDefaults, 'maxRangeMonths'>,
  today: string = todayInSaoPaulo(),
): ImportFieldErrors => {
  const errors: ImportFieldErrors = {}

  const hasStart = isValidDateOnly(values.startDate)
  const hasEnd = isValidDateOnly(values.endDate)

  if (!hasStart) errors.startDate = 'validation.importStartDateRequired'
  if (!hasEnd) errors.endDate = 'validation.importEndDateRequired'

  // Data final no futuro (FR-011): só com as duas datas presentes e reais.
  if (hasEnd && values.endDate > today) errors.endDate = 'validation.importEndDateInFuture'

  if (hasStart && hasEnd && errors.endDate === undefined) {
    if (values.startDate > values.endDate) {
      errors.startDate = 'validation.importStartDateAfterEndDate'
    } else {
      // Limite em MESES corridos (FR-010), com a mesma conta do servidor: comparar
      // `endDate` com o teto calculado a partir de `startDate` em vez de contar dias,
      // porque "3 meses" são 89 a 92 dias conforme o mês.
      const maxEnd = maxEndDateForStart(values.startDate, defaults.maxRangeMonths)

      if (maxEnd !== null && values.endDate > maxEnd) {
        errors.endDate = 'validation.importRangeTooLong'
      }
    }
  }

  return errors
}

/** `true` quando o formulário pode ser enviado. */
export const isImportFormValid = (errors: ImportFieldErrors): boolean =>
  Object.values(errors).every((message) => message === undefined)

/**
 * Período trazido pela query string — `/importacao?inicio=AAAA-MM-DD&fim=AAAA-MM-DD`,
 * o destino de "Repetir este bloco" (FR-026).
 *
 * Data inválida é IGNORADA (o teste T071 é explícito sobre isso): um link
 * manualmente editado ou corrompido tem de levar ao formulário com as sugestões
 * normais, não a uma tela com datas absurdas pré-preenchidas. Login e senha nunca
 * vêm por URL, em nenhuma hipótese (FR-007).
 */
export const periodFromParams = (params: URLSearchParams): ImportPeriod | null => {
  const start = params.get(IMPORT_QUERY_START_KEY)
  const end = params.get(IMPORT_QUERY_END_KEY)

  if (start === null || end === null) return null
  if (!isValidDateOnly(start) || !isValidDateOnly(end)) return null
  if (start > end) return null

  return { start, end }
}

/** Monta a query de "Repetir este bloco" (o destino do botão do relatório). */
export const repeatBlockHref = (period: ImportPeriod): string =>
  `/importacao?${IMPORT_QUERY_START_KEY}=${period.start}&${IMPORT_QUERY_END_KEY}=${period.end}`

/**
 * Formata `AAAA-MM-DD` como `dd/mm/aaaa`. Delegate de `lib/dates.formatDateOnly`,
 * que monta o texto a partir dos componentes (sem `Date` nem `Intl`): o mesmo
 * dia-calendário sai igual em qualquer fuso de navegador (FR-020 da feature 002).
 */
export const formatImportDate = (value: string | null): string => formatDateOnly(value)

/**
 * Instante ISO → `HH:mm` em São Paulo, para o horário de liberação do bloqueio
 * (FR-008a). O mesmo fuso da regra de data: o horário tem de ser o do pulso da
 * vendedora, não o do servidor nem o do navegador.
 */
export const formatReleaseTime = (iso: string | null): string => {
  const when = parseUtcInstant(iso)
  if (when === null) return ''

  return new Intl.DateTimeFormat('pt-BR', {
    timeZone: 'America/Sao_Paulo',
    hour: '2-digit',
    minute: '2-digit',
  }).format(when)
}

/** Instante ISO → `dd/mm/aaaa HH:mm` em São Paulo (histórico e relatório). */
export const formatImportInstant = (iso: string | null): string => {
  const when = parseUtcInstant(iso)
  if (when === null) return ''

  return new Intl.DateTimeFormat('pt-BR', {
    timeZone: 'America/Sao_Paulo',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(when)
}

/** Sufixo de fuso explícito (`Z` ou `±hh:mm`) no fim de um instante ISO. */
const TIME_ZONE_SUFFIX = /(?:Z|[+-]\d{2}:?\d{2})$/i

/**
 * Lê um instante da API como UTC. As colunas são `timestamp without time zone`
 * gravadas em UTC (constituição §V) e o `DateTime` sai sem `Z` no JSON; sem o
 * sufixo, `new Date()` leria o valor como hora LOCAL do navegador e o horário de
 * liberação ficaria deslocado em 3 horas.
 */
export const parseUtcInstant = (iso: string | null | undefined): Date | null => {
  if (iso === null || iso === undefined || iso.trim() === '') return null

  const normalized = TIME_ZONE_SUFFIX.test(iso) ? iso : `${iso}Z`
  const when = new Date(normalized)

  return Number.isNaN(when.getTime()) ? null : when
}

/** As nove contagens, na ordem de exibição do progresso e do relatório (FR-047). */
export const COUNT_KEYS: readonly (keyof ImportRunCounts)[] = [
  'inserted',
  'updated',
  'unchanged',
  'missing',
  'failed',
  'conflicts',
  'customersCreated',
  'imagesStored',
  'imagesFailed',
]

/**
 * Contagens com versão "seria…" na simulação (FR-032). As demais (não
 * encontrados, erros, conflitos, fotos com erro) mantêm o rótulo normal — o
 * cabeçalho "Simulação" já diz que nada foi gravado.
 */
const DRY_RUN_COUNT_KEYS: ReadonlySet<keyof ImportRunCounts> = new Set<keyof ImportRunCounts>([
  'inserted',
  'updated',
  'unchanged',
  'customersCreated',
  'imagesStored',
])

/** Chave i18n do rótulo de uma contagem, no condicional quando é simulação. */
export const countLabelKey = (key: keyof ImportRunCounts, dryRun: boolean): string =>
  dryRun && DRY_RUN_COUNT_KEYS.has(key) ? `import.wouldCounts.${key}` : `import.counts.${key}`

/** Pedidos gravados de um bloco ou execução: inseridos + atualizados + inalterados (US3-1). */
export const ordersWrittenOf = (counts: ImportRunCounts): number =>
  counts.inserted + counts.updated + counts.unchanged
