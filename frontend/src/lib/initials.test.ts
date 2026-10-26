import { describe, expect, it } from 'vitest'

import { initials } from '@/lib/initials'

/**
 * `initials` (FR-010, T046): ate duas maiusculas das duas primeiras palavras,
 * usadas no fallback do avatar quando o cliente nao tem foto.
 */
describe('initials', () => {
  it('pega a primeira letra das duas primeiras palavras', () => {
    expect(initials('Maria da Silva')).toBe('MD')
  })

  it('um nome simples gera uma letra', () => {
    expect(initials('ana')).toBe('A')
  })

  it('entrada vazia ou só espaços gera o placeholder', () => {
    expect(initials('')).toBe('?')
    expect(initials('   ')).toBe('?')
    expect(initials('\t\n ')).toBe('?')
  })

  it('ignora espaços múltiplos entre as palavras', () => {
    expect(initials('  João   Cardoso  ')).toBe('JC')
  })

  it('usa apenas as duas primeiras palavras de nomes longos', () => {
    expect(
      initials('Maria Aparecida da Conceição dos Santos Ferreira Albuquerque'),
    ).toBe('MA')
  })

  it('preserva a acentuação em maiúscula', () => {
    expect(initials('Úrsula Kelmer')).toBe('ÚK')
  })
})
