import { describe, expect, it } from 'vitest'

import { ApiError } from '@/Services/apiClient'
import {
  fieldErrorsOf,
  returnToOf,
  validateLoginForm,
  validateRegisterForm,
} from '@/lib/authForm'

const REGISTER_BASE = {
  name: 'Ana',
  email: 'ana@exemplo.com',
  password: 'senha-forte-1',
  confirmPassword: 'senha-forte-1',
}

describe('returnToOf', () => {
  it('devolve o returnTo quando o estado o traz', () => {
    expect(returnToOf({ returnTo: '/pedidos?pagina=2' })).toBe('/pedidos?pagina=2')
  })

  it('ignora estado sem returnTo', () => {
    expect(returnToOf({})).toBeUndefined()
    expect(returnToOf({ destino: '/clientes' })).toBeUndefined()
  })

  it('ignora estado nulo ou não-objeto', () => {
    expect(returnToOf(null)).toBeUndefined()
    expect(returnToOf(undefined)).toBeUndefined()
    expect(returnToOf('/clientes')).toBeUndefined()
    expect(returnToOf(7)).toBeUndefined()
  })

  it('ignora returnTo que não é string não vazia', () => {
    expect(returnToOf({ returnTo: '' })).toBeUndefined()
    expect(returnToOf({ returnTo: 42 })).toBeUndefined()
    expect(returnToOf({ returnTo: { path: '/clientes' } })).toBeUndefined()
  })
})

describe('fieldErrorsOf', () => {
  it('mapeia erros da API por campo', () => {
    const err = new ApiError(400, 'Dados inválidos.', [
      { campo: 'name', mensagem: 'O nome é obrigatório.' },
      { campo: 'email', mensagem: 'E-mail inválido.' },
      { campo: 'password', mensagem: 'A senha deve ter entre 8 e 128 caracteres.' },
    ])

    expect(fieldErrorsOf(err)).toEqual({
      name: 'O nome é obrigatório.',
      email: 'E-mail inválido.',
      password: 'A senha deve ter entre 8 e 128 caracteres.',
    })
  })

  it('compara o nome do campo sem diferenciar maiúsculas', () => {
    const err = new ApiError(400, 'Dados inválidos.', [
      { campo: 'Email', mensagem: 'E-mail inválido.' },
    ])

    expect(fieldErrorsOf(err)).toEqual({ email: 'E-mail inválido.' })
  })

  it('ignora campos que o formulário não conhece', () => {
    const err = new ApiError(400, 'Dados inválidos.', [
      { campo: 'body', mensagem: 'Valor inválido.' },
      { campo: 'confirmPassword', mensagem: 'As senhas não conferem.' },
    ])

    expect(fieldErrorsOf(err)).toEqual({})
  })

  it('devolve objeto vazio para ApiError sem erros (409/401) e para outros tipos', () => {
    expect(fieldErrorsOf(new ApiError(409, 'Este e-mail já está cadastrado.', []))).toEqual({})
    expect(fieldErrorsOf(new Error('qualquer coisa'))).toEqual({})
    expect(fieldErrorsOf(undefined)).toEqual({})
    expect(fieldErrorsOf(null)).toEqual({})
    expect(fieldErrorsOf('string')).toEqual({})
  })
})

// US1 · FR-003 — as regras do cliente espelham o validator do backend (data-model §3/§8):
// o formulário não envia nada enquanto houver erro de formato.
describe('validateRegisterForm', () => {
  it('nome vazio ou só espaços pede o nome', () => {
    expect(validateRegisterForm({ ...REGISTER_BASE, name: '' })).toEqual({
      name: 'validation.nameRequired',
    })
    expect(validateRegisterForm({ ...REGISTER_BASE, name: '   ' })).toEqual({
      name: 'validation.nameRequired',
    })
  })

  it('nome com 121 caracteres é recusado e com 120 passa', () => {
    expect(validateRegisterForm({ ...REGISTER_BASE, name: 'n'.repeat(121) })).toEqual({
      name: 'validation.nameTooLong',
    })
    expect(validateRegisterForm({ ...REGISTER_BASE, name: 'n'.repeat(120) })).toEqual({})
  })

  it('e-mail vazio pede o e-mail', () => {
    expect(validateRegisterForm({ ...REGISTER_BASE, email: '' })).toEqual({
      email: 'validation.emailRequired',
    })
  })

  it('e-mail sem @ ou sem domínio é inválido', () => {
    for (const email of ['ana', 'ana@', 'ana@exemplo', '@exemplo.com', 'a b@exemplo.com']) {
      expect(validateRegisterForm({ ...REGISTER_BASE, email })).toEqual({
        email: 'validation.emailInvalid',
      })
    }
  })

  it('e-mail com 255 caracteres é recusado', () => {
    const local = 'a'.repeat(243)
    const email = `${local}@exemplo.com`

    expect(email).toHaveLength(255)
    expect(validateRegisterForm({ ...REGISTER_BASE, email })).toEqual({
      email: 'validation.emailTooLong',
    })
  })

  it('senha com 7 ou 129 caracteres pede o comprimento; 8 e 128 passam', () => {
    for (const password of ['1234567', 'a'.repeat(129)]) {
      expect(
        validateRegisterForm({
          ...REGISTER_BASE,
          password,
          confirmPassword: password,
        }),
      ).toEqual({ password: 'validation.passwordLength' })
    }

    for (const password of ['12345678', 'a'.repeat(128)]) {
      expect(
        validateRegisterForm({
          ...REGISTER_BASE,
          password,
          confirmPassword: password,
        }),
      ).toEqual({})
    }
  })

  // Edge case da spec: a senha NUNCA é cortada — espaços nas pontas são parte dela.
  it('senha com espaços nas pontas preservada é válida', () => {
    const password = '  senha com espacos  '

    expect(
      validateRegisterForm({ ...REGISTER_BASE, password, confirmPassword: password }),
    ).toEqual({})
  })

  it('confirmação diferente da senha avisa que não conferem', () => {
    expect(validateRegisterForm({ ...REGISTER_BASE, confirmPassword: 'senha-forte-2' })).toEqual({
      confirmPassword: 'validation.passwordMismatch',
    })
  })

  it('tudo válido devolve objeto vazio', () => {
    expect(validateRegisterForm(REGISTER_BASE)).toEqual({})
  })
})
// US2 · FR-002 / data-model §8 — a tela de entrada só exige formato de e-mail e
// senha preenchida. O comprimento da senha NÃO é validado: qualquer senha errada
// tem de cair no 401 genérico da API, sem o cliente recusar antes.
describe('validateLoginForm', () => {
  it('e-mail vazio pede o e-mail', () => {
    expect(validateLoginForm({ email: '', password: 'senha-forte-1' })).toEqual({
      email: 'validation.emailRequired',
    })
    expect(validateLoginForm({ email: '   ', password: 'senha-forte-1' })).toEqual({
      email: 'validation.emailRequired',
    })
  })

  it('e-mail mal formado é inválido', () => {
    for (const email of ['ana', 'ana@', 'ana@exemplo', 'a b@exemplo.com']) {
      expect(validateLoginForm({ email, password: 'senha-forte-1' })).toEqual({
        email: 'validation.emailInvalid',
      })
    }
  })

  it('senha vazia pede a senha', () => {
    expect(validateLoginForm({ email: 'ana@exemplo.com', password: '' })).toEqual({
      password: 'validation.passwordRequired',
    })
  })

  it('senha curta é ACEITA no login (comprimento não validado)', () => {
    expect(validateLoginForm({ email: 'ana@exemplo.com', password: '123' })).toEqual({})
  })

  it('e-mail longo demais usa a mesma regra do cadastro', () => {
    const email = `${'a'.repeat(243)}@exemplo.com`

    expect(email).toHaveLength(255)
    expect(validateLoginForm({ email, password: 'senha-forte-1' })).toEqual({
      email: 'validation.emailTooLong',
    })
  })

  it('tudo válido devolve objeto vazio', () => {
    expect(validateLoginForm({ email: 'ana@exemplo.com', password: 'senha-forte-1' })).toEqual({})
  })
})
