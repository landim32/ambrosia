import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/Contexts/AuthContext'
import { useAuth } from '@/hooks/useAuth'
import { apiRequest, setUnauthorizedHandler } from '@/Services/apiClient'
import { authService } from '@/Services/authService'
import { AUTH_STORAGE_KEY, readSession, writeSession } from '@/Services/authStorage'

import type { AuthSession, LoginResult, UserInfo } from '@/types/auth'

/**
 * O serviço de autenticação é substituído: aqui se testam as TRANSIÇÕES de
 * sessão (data-model §3), não o chamamento HTTP (coberto em authService).
 */
vi.mock('@/Services/authService', () => ({
  authService: { loginWithGoogle: vi.fn(), loginDev: vi.fn() },
}))

const USER: UserInfo = {
  id: 11,
  email: 'ana@exemplo.com',
  name: 'Ana',
  provider: 'dev',
  createdAt: '2026-01-01T00:00:00Z',
}

const sessionExpiringIn = (ms: number): AuthSession => ({
  token: 'jwt-da-sessao',
  expiresAt: new Date(Date.now() + ms).toISOString(),
  user: USER,
})

const loginResultExpiringIn = (ms: number): LoginResult => ({
  token: 'jwt-novo',
  expiresAt: new Date(Date.now() + ms).toISOString(),
  user: USER,
})

const LocationProbe = () => {
  const location = useLocation()

  return (
    <div data-testid="location">
      {`LOC:${location.pathname}${location.search}:${JSON.stringify(location.state ?? null)}`}
    </div>
  )
}

const StatusProbe = () => {
  const { isAuthenticated, loading, error, clearError } = useAuth()

  return (
    <div>
      <div data-testid="status">
        {`auth=${String(isAuthenticated)} loading=${String(loading)} error=${error ?? '-'}`}
      </div>
      <button
        type="button"
        onClick={() => {
          clearError()
        }}
      >
        limpar-erro
      </button>
    </div>
  )
}

const ActionsProbe = () => {
  const { loginDev, loginWithGoogle, logout } = useAuth()

  return (
    <>
      <button
        type="button"
        onClick={() => {
          void loginDev('ana@exemplo.com', 'Ana')
        }}
      >
        login-dev
      </button>
      <button
        type="button"
        onClick={() => {
          void loginWithGoogle('id-token-falso')
        }}
      >
        login-google
      </button>
      <button
        type="button"
        onClick={() => {
          logout()
        }}
      >
        sair
      </button>
      <button
        type="button"
        onClick={() => {
          logout('expirada')
        }}
      >
        expirar
      </button>
      <button
        type="button"
        onClick={() => {
          void apiRequest('GET', '/customers').catch(() => undefined)
        }}
      >
        chamada-autenticada
      </button>
    </>
  )
}

const renderAuth = (initialPath = '/clientes') =>
  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <AuthProvider>
        <LocationProbe />
        <StatusProbe />
        <ActionsProbe />
      </AuthProvider>
    </MemoryRouter>,
  )

const locationText = (): string => screen.getByTestId('location').textContent

beforeEach(() => {
  localStorage.clear()
  vi.mocked(authService.loginDev).mockResolvedValue(loginResultExpiringIn(60_000))
  vi.mocked(authService.loginWithGoogle).mockResolvedValue(loginResultExpiringIn(60_000))
})

afterEach(() => {
  setUnauthorizedHandler(undefined)
  localStorage.clear()
})

describe('AuthContext', () => {
  it('sessão válida no storage autentica e não navega', () => {
    writeSession(sessionExpiringIn(60_000))
    renderAuth('/pedidos?tipo=sale')

    expect(screen.getByTestId('status')).toHaveTextContent('auth=true')
    expect(locationText()).toBe('LOC:/pedidos?tipo=sale:null')
  })

  it('chave corrompida no storage descarta a sessão com o motivo expirada', async () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '{"token":"","expiresAt":"ontem"}')
    renderAuth('/pedidos?tipo=sale')

    await waitFor(() => {
      expect(locationText()).toBe('LOC:/login?motivo=expirada:{"returnTo":"/pedidos?tipo=sale"}')
    })
    expect(readSession()).toBeNull()
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('sem nenhuma chave fica anônima e sem aviso de expiração', () => {
    renderAuth('/pedidos?tipo=sale')

    expect(screen.getByTestId('status')).toHaveTextContent('auth=false')
    expect(locationText()).toBe('LOC:/pedidos?tipo=sale:null')
  })

  it('loginDev grava a sessão e autentica', async () => {
    const events = userEvent.setup()
    renderAuth('/login')

    await events.click(screen.getByRole('button', { name: 'login-dev' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('auth=true')
    })
    expect(vi.mocked(authService.loginDev)).toHaveBeenCalledWith('ana@exemplo.com', 'Ana')
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toContain('jwt-novo')
  })

  it('loginWithGoogle envia o idToken e autentica', async () => {
    const events = userEvent.setup()
    renderAuth('/login')

    await events.click(screen.getByRole('button', { name: 'login-google' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('auth=true')
    })
    expect(vi.mocked(authService.loginWithGoogle)).toHaveBeenCalledWith('id-token-falso')
  })

  it('login recusado mantém a vendedora anônima, sem gravar sessão', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.loginDev).mockRejectedValueOnce(new Error('credencial recusada'))
    renderAuth('/login')

    await events.click(screen.getByRole('button', { name: 'login-dev' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('auth=false')
    })
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('clearError volta a não expor erro', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.loginDev).mockRejectedValueOnce(new Error('credencial recusada'))
    renderAuth('/login')

    await events.click(screen.getByRole('button', { name: 'login-dev' }))
    await waitFor(() => {
      expect(screen.getByTestId('status')).not.toHaveTextContent('error=-')
    })

    await events.click(screen.getByRole('button', { name: 'limpar-erro' }))
    expect(screen.getByTestId('status')).toHaveTextContent('error=-')
  })

  it('logout() limpa o storage e navega para /login sem motivo', async () => {
    const events = userEvent.setup()
    writeSession(sessionExpiringIn(60_000))
    renderAuth('/pedidos?tipo=sale')

    await events.click(screen.getByRole('button', { name: 'sair' }))

    expect(locationText()).toBe('LOC:/login:null')
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
    expect(screen.getByTestId('status')).toHaveTextContent('auth=false')
  })

  it("logout('expirada') navega para /login?motivo=expirada com state.returnTo", async () => {
    const events = userEvent.setup()
    writeSession(sessionExpiringIn(60_000))
    renderAuth('/pedidos?tipo=sale')

    await events.click(screen.getByRole('button', { name: 'expirar' }))

    expect(locationText()).toBe('LOC:/login?motivo=expirada:{"returnTo":"/pedidos?tipo=sale"}')
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('expira pelo timer agendado para expiresAt, sem chamar a API', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] })
    try {
      vi.setSystemTime(new Date('2026-03-01T10:00:00Z'))
      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{}'))
      localStorage.setItem(
        AUTH_STORAGE_KEY,
        JSON.stringify({ token: 'jwt', expiresAt: '2026-03-01T10:00:30.000Z', user: USER }),
      )
      renderAuth('/clientes')

      expect(screen.getByTestId('status')).toHaveTextContent('auth=true')

      await vi.advanceTimersByTimeAsync(31_000)

      expect(locationText()).toBe('LOC:/login?motivo=expirada:{"returnTo":"/clientes"}')
      expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
      expect(fetchSpy).not.toHaveBeenCalled()
    } finally {
      vi.useRealTimers()
    }
  })

  it('401 vindo do apiClient encerra a sessão com o motivo expirada', async () => {
    const events = userEvent.setup()
    vi.spyOn(globalThis, 'fetch').mockImplementation(() =>
      Promise.resolve(new Response('', { status: 401 })),
    )
    writeSession(sessionExpiringIn(60_000))
    renderAuth('/clientes')

    await events.click(screen.getByRole('button', { name: 'chamada-autenticada' }))

    await waitFor(() => {
      expect(locationText()).toBe('LOC:/login?motivo=expirada:{"returnTo":"/clientes"}')
    })
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })
})
