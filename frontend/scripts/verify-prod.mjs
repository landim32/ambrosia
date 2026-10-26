import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

/**
 * `npm run verify:prod` — prova da SC-008 / FR-002 (T091).
 *
 * A entrada de desenvolvimento some do pacote por dois caminhos: o chunk de
 * `DevLoginForm` atras de `VITE_DEV_LOGIN === 'true'` (Research §8) e a guarda do
 * mesmo tipo em `Services/authService.ts`. Este script le todo arquivo JS dentro
 * de `dist/` e FALHA se qualquer termo abaixo aparecer — "escondido" nao e o
 * mesmo que "ausente". Rodar depois de `npm run build -- --mode production`.
 *
 * O nome do metodo `loginDev` sobrevive a minificacao (e chave de objeto vista
 * pelo `AuthContext`): o que nao pode sobreviver e o CHAMADOR, isto e, a rota
 * `/auth/dev-login` e o chunk do formulario. Codigo de saida 1 = pacote de
 * producao inaceitavel.
 */
const DIST = resolve(import.meta.dirname, '..', 'dist')

/** Termos proibidos no pacote de producao (tasks T091). */
const FORBIDDEN = ['dev-login', 'DevLoginForm']

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
    `verify:prod — ${DIST} não existe. Rode "npm run build -- --mode production" antes.`,
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
  console.error('\nFAIL — código de desenvolvimento no pacote de produção:')
  for (const line of offenders) console.error(`  ${line}`)
  console.error('\nConfirme VITE_DEV_LOGIN ausente e a guarda em src/Services/authService.ts.')
  process.exit(1)
}

console.log(
  `\nOK — nenhuma ocorrência de ${FORBIDDEN.map((term) => `"${term}"`).join(' / ')} em dist/.`,
)
