/**
 * Iniciais do avatar do cliente (FR-010, T050).
 *
 * Ate duas letras MAIUSCULAS das duas primeiras palavras. Sem foto (HU 010),
 * esse e o conteudo do `AvatarFallback`. Nome vazio devolve `?` — o avatar nunca
 * fica em branco.
 */
export const initials = (name: string): string => {
  const words = name
    .trim()
    .split(/\s+/)
    .filter((word) => word !== '')

  if (words.length === 0) return '?'

  const letters = words
    .slice(0, 2)
    .map((word) => Array.from(word)[0] ?? '')
    .filter((letter) => letter !== '')
    .map((letter) => letter.toLocaleUpperCase('pt-BR'))

  return letters.length === 0 ? '?' : letters.join('')
}
