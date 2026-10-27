import { describe, expect, it } from 'vitest'

import { isMaskedCpf, maskedCpfShape } from '@/lib/cpf'

/**
 * `cpf` (FR-012/FR-022, T046): o frontend NUNCA desmascara nem monta um CPF
 * completo a partir do mascarado — so reconhece o formato da API para exibir.
 */
describe('isMaskedCpf', () => {
  it('aceita o formato que a API devolve', () => {
    expect(isMaskedCpf('***.456.789-**')).toBe(true)
    expect(isMaskedCpf('***.000.000-**')).toBe(true)
  })

  it('recusa qualquer outra coisa', () => {
    expect(isMaskedCpf('123.456.789-09')).toBe(false)
    expect(isMaskedCpf('12345678909')).toBe(false)
    expect(isMaskedCpf('***.456.789-09')).toBe(false)
    expect(isMaskedCpf('***456789**')).toBe(false)
    expect(isMaskedCpf('')).toBe(false)
    expect(isMaskedCpf('   ')).toBe(false)
  })

  it('recusa texto com o padrão no meio', () => {
    expect(isMaskedCpf('cpf: ***.456.789-**')).toBe(false)
  })
})

describe('maskedCpfShape', () => {
  it('devolve o proprio texto quando ja esta mascarado', () => {
    expect(maskedCpfShape('***.456.789-**')).toBe('***.456.789-**')
  })

  /**
   * Regiao de seguranca: um valor que NAO e o mascarado da API (bug de
   * serializacao, dado manualmente inserido) nao pode vazar o CPF completo na
   * listagem — sai como o placeholder neutro (FR-022).
   */
  it('nunca devolve um CPF completo', () => {
    expect(maskedCpfShape('123.456.789-09')).toBe('***.***.***-**')
    expect(maskedCpfShape('12345678909')).toBe('***.***.***-**')
    expect(maskedCpfShape('')).toBe('***.***.***-**')
  })

  it('nenhuma saida de maskedCpfShape tem 11 digitos', () => {
    for (const input of ['123.456.789-09', '12345678909', '***.456.789-**']) {
      expect(maskedCpfShape(input).replace(/\D/g, '').length).toBeLessThan(11)
    }
  })
})
