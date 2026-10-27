import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

const roots = process.argv.slice(2)
const exts = ['.ts', '.tsx', '.css', '.js', '.mjs', '.json', '.html']

const walk = (dir, out) => {
  for (const entry of readdirSync(dir)) {
    if (entry === 'node_modules' || entry === 'dist' || entry.startsWith('.')) continue
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) walk(full, out)
    else if (exts.some((e) => entry.endsWith(e))) out.push(full)
  }
  return out
}

const collect = () => {
  const files = []
  for (const root of roots) {
    const full = resolve(root)
    if (statSync(full).isDirectory()) walk(full, files)
    else if (exts.some((e) => full.endsWith(e))) files.push(full)
  }
  return files
}

let problems = 0

for (const file of collect()) {
  const bytes = readFileSync(file)
  const text = bytes.toString('utf8')
  const bom = bytes.length > 2 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf
  const mojibake = /Ã©|Ã¨|Ã¡|Ã£|Ã§|Ã´|Ãª|Ã­|Ã¸|Â[ °¡-¿]|â€|â‰|Ã§o/.test(text)
  if (bom || mojibake) {
    problems += 1
    console.log(`${bom ? 'BOM ' : ''}${mojibake ? 'MOJIBAKE ' : ''}${file}`)
  }
}

console.log(
  problems === 0 ? `encoding OK (${collect().length} files)` : `${problems} file(s) with problems`,
)
