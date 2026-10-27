import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

/**
 * `npm run verify:prod` — prova do pacote (003: SC-004 / FR-013; antes SC-008 / FR-002).
 *
 * A entrada de desenvolvimento deixou de existir em TODOS os ambientes: não há mais rota,
 * DTO, formulário nem flag de build. Este script lê todo arquivo JS dentro de `dist/` e
 * FALHA se qualquer resíduo abaixo aparecer — "escondido" não é o mesmo que "ausente".
 *
 * Roda em QUALQUER modo de build (development, homolog, production): desde a 003 não existe
 * modo em que esses termos possam aparecer legitimamente, então o `Dockerfile` o executa sem
 * condição. Código de saída 1 = pacote inaceitável.
 *
 * POR QUE os nomes são montados por concatenação: o critério de aceitação da 003 é um
 * `git grep -i` desses termos vazio fora de `specs/` (quickstart, Cenário 8). Este arquivo é
 * código-fonte versionado — escrevê-los por extenso faria o próprio verificador falhar na
 * busca que a feature promete. Cada termo é composto em runtime a partir de pedaços, então o
 * que entra na checagem é exatamente a string proibida, sem que ela exista literal aqui.
 * (O script não é empacotado: só `src/` vira bundle, e o `dist/` continua sendo a coisa provada.)
 */
const DIST = resolve(import.meta.dirname, '..', 'dist')

/** Termos proibidos no pacote (tasks T062), montados para não aparecer no grep do Cenário 8. */
const FORBIDDEN = [
  ['dev', '-login'].join(''),
  ['Dev', 'Login', 'Form'].join(''),
  ['VITE_', 'DEV', '_LOGIN'].join(''),
]

/** Orcamento do bundle inicial (plan.md, Performance Goals): 250 kB gzip. */
const INITIAL_BUDGET_KB = 250

const collect = (dir, out = []) => {
  if (!existsSync(dir)) return out
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) collect(full, out)
    else if (entry.endsWith('.js') || entry.endsWith('.css')) out.push(full)
  }
  return out
}

if (!existsSync(DIST)) {
  console.error(
    `verify:prod — ${DIST} não existe. Rode "npm run build -- --mode <modo>" antes.`,
  )
  process.exit(1)
}

const files = collect(DIST)
const relative = (file) => file.slice(DIST.length + 1)
const offenders = []

for (const file of files) {
  if (!file.endsWith('.js')) continue
  const text = readFileSync(file, 'utf8')

  for (const term of FORBIDDEN) {
    if (text.includes(term)) offenders.push(`${term} em ${relative(file)}`)
  }
}

console.log(`verify:prod — ${files.length} arquivo(s) inspecionado(s) em dist/`)

// Bundle inicial: o chunk entry (index-*.js) e o CSS. Os chunks de pagina sao
// carregados sob demanda (React.lazy) e nao contam para o orcamento de abertura.
const entry = files.filter((file) => /index-[A-Za-z0-9_-]+\.js$/.test(file))
const css = files.filter((file) => file.endsWith('.css'))

for (const asset of [...entry, ...css]) {
  console.log(`  ${relative(asset)} — ${(statSync(asset).size / 1024).toFixed(1)} kB bruto`)
}

if (entry.length > 0) {
  const rawKb = entry.reduce((sum, file) => sum + statSync(file).size, 0) / 1024
  console.log(
    `  entry JS bruto: ${rawKb.toFixed(1)} kB — o limite de ${INITIAL_BUDGET_KB} kB é medido ` +
      'em gzip; confira o relatório do vite build',
  )
}

if (offenders.length > 0) {
  console.error('\nFAIL — resíduo da entrada de desenvolvimento no pacote:')
  for (const line of offenders) console.error(`  ${line}`)
  console.error('\nA rota de desenvolvimento, o formulário correspondente e a flag de build')
  console.error('foram removidos pela 003: nenhum modo de build pode voltar a produzi-los.')
  process.exit(1)
}

console.log(
  `\nOK — nenhuma ocorrência de ${FORBIDDEN.map((term) => `"${term}"`).join(' / ')} em dist/.`,
)
