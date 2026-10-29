const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/

interface DateParts {
  year: number
  month: number
  day: number
}

const pad2 = (value: number): string => String(value).padStart(2, '0')

/** Extrai e valida os três componentes de `AAAA-MM-DD`; `null` se irreais. */
const readParts = (value: string | null | undefined): DateParts | null => {
  if (typeof value !== 'string') return null

  const match = DATE_ONLY_PATTERN.exec(value)
  if (!match) return null

  const groups = match.slice(1)
  const yearText = groups[0]
  const monthText = groups[1]
  const dayText = groups[2]
  if (!yearText || !monthText || !dayText) return null

  const year = Number(yearText)
  const month = Number(monthText)
  const day = Number(dayText)

  if (month < 1 || month > 12 || day < 1) return null

  // Rejeita dias inexistentes (29/02 em ano não bissexto, 31/04, 99 do mês...).
  const probe = new Date(year, month - 1, day)
  const real =
    probe.getFullYear() === year && probe.getMonth() === month - 1 && probe.getDate() === day

  return real ? { year, month, day } : null
}

/**
 * Valida `AAAA-MM-DD` como dia-calendário real (não como instante).
 * `2026-13-99`, `2026-02-30` e formatos com hora são rejeitados.
 */
export const isValidDateOnly = (value: string | null | undefined): boolean =>
  readParts(value) !== null

/**
 * Converte `AAAA-MM-DD` em `Date` local **sem deslocamento de fuso** (FR-020).
 * Nunca use `new Date(iso)`: em fusos negativos isso joga a data para o dia
 * anterior. Retorna `null` para entrada inválida.
 */
export const parseDateOnly = (value: string | null | undefined): Date | null => {
  const parts = readParts(value)
  if (!parts) return null

  return new Date(parts.year, parts.month - 1, parts.day)
}

/**
 * `AAAA-MM-DD` → `dd/mm/aaaa`. Montado a partir dos componentes do texto (nunca
 * via `Intl`/`Date`): o resultado é o MESMO dia-calendário em qualquer fuso, que
 * é exatamente o que a FR-020 e a SC-007 exigem.
 */
export const formatDateOnly = (value: string | null | undefined): string => {
  const parts = readParts(value)
  if (!parts) return ''

  return `${pad2(parts.day)}/${pad2(parts.month)}/${String(parts.year)}`
}

/**
 * Instante ISO → `dd de mmm. de aaaa` no fuso do navegador (feature 009: a data
 * da foto na grade e no visualizador). Diferente de `formatDateOnly`, aqui o
 * valor É um instante, então o deslocamento de fuso é desejável — e o mês por
 * extenso vem do locale (como a moeda em `lib/currency.ts`), não de string no
 * código. `''` quando o texto não é um instante parseável.
 */
const longDateFormatter = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
})

export const formatDateLong = (value: string | null | undefined): string => {
  if (typeof value !== 'string' || value === '') return ''

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''

  return longDateFormatter.format(date)
}

/**
 * Instante ISO → `dd/mm/aaaa HH:mm` no fuso do navegador (uso eventual).
 * `''` quando o texto não é um instante parseável.
 */
export const formatDateTime = (value: string | null | undefined): string => {
  if (typeof value !== 'string' || value === '') return ''

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''

  return [
    pad2(date.getDate()),
    '/',
    pad2(date.getMonth() + 1),
    '/',
    String(date.getFullYear()),
    ' ',
    pad2(date.getHours()),
    ':',
    pad2(date.getMinutes()),
  ].join('')
}
