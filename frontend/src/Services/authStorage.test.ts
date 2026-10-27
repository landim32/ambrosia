import { afterEach, describe, expect, it } from 'vitest'

import {
  AUTH_STORAGE_KEY,
  clearSession,
  readSession,
  writeSession,
} from '@/Services/authStorage'
import type { AuthSession } from '@/types/auth'

const VALID: AuthSession = {
  token: 'jwt',
  expiresAt: '2099-01-01T00:00:00Z',
  user: {
    id: 1,
    email: 'ana@exemplo.com',
    name: 'Ana',
    provider: 'password',
    createdAt: '2026-01-01T00:00:00Z',
  },
}

const raw = (value: unknown): void => {
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(value))
}

afterEach(() => {
  localStorage.clear()
})

describe('authStorage', () => {
  it('usa a chave da constituição §VI', () => {
    expect(AUTH_STORAGE_KEY).toBe('login-with-metamask:auth')
  })

  it('grava e lê uma sessão válida', () => {
    writeSession(VALID)
    expect(readSession()).toEqual(VALID)
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toContain('"token":"jwt"')
  })

  it('descarta JSON inválido e remove a chave', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '{isso não é json')
    expect(readSession()).toBeNull()
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('descarta token vazio', () => {
    raw({ ...VALID, token: '' })
    expect(readSession()).toBeNull()
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('descarta expiresAt não parseável', () => {
    raw({ ...VALID, expiresAt: 'ontem' })
    expect(readSession()).toBeNull()
  })

  it('descarta expiresAt no passado', () => {
    raw({ ...VALID, expiresAt: '2020-01-01T00:00:00Z' })
    expect(readSession()).toBeNull()
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('descarta user faltando ou sem e-mail', () => {
    raw({ token: 'jwt', expiresAt: '2099-01-01T00:00:00Z' })
    expect(readSession()).toBeNull()

    raw({
      ...VALID,
      user: { id: 1, email: '', name: 'Ana', provider: 'password', createdAt: '' },
    })
    expect(readSession()).toBeNull()
  })

  it('descarta provider desconhecido', () => {
    raw({ ...VALID, user: { ...VALID.user, provider: 'facebook' } })
    expect(readSession()).toBeNull()
  })

  it('clear() remove a chave', () => {
    writeSession(VALID)
    clearSession()
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
    expect(readSession()).toBeNull()
  })
})
