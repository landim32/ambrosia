import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/Contexts/AuthContext'
import { useAuth } from '@/hooks/useAuth'
import { ApiError, apiRequest, setUnauthorizedHandler } from '@/Services/apiClient'
import { authService } from '@/Services/authService'
import { AUTH_STORAGE_KEY, readSession, writeSession } from '@/Services/authStorage'

import type { AuthSession, LoginResult, UserInfo } from '@/types/auth'

/**
 * O serviço de autenticação é substituído: aqui se testam as TRANSIÇÕES de
 * sessão (data-model §3), não o chamamento HTTP (coberto em authService).
 */
vi.mock('@/Services/authService', () => ({
  authService: {
    loginWithGoogle: vi.fn(),
    loginWithPassword: vi.fn(),
    register: vi.fn(),
  },
}))

const USER: UserInfo = {
  id: 11,
  email: 'ana@exemplo.com',
  name: 'Ana',
  provider: 'password',
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
  const { isAuthenticated, loading, error, errorFields, clearError } = useAuth()

  return (
    <div>
      <div data-testid="status">
        {`auth=${String(isAuthenticated)} loading=${String(loading)} error=${error ?? '-'}`}
      </div>
      <div data-testid="fields">{`fields=${JSON.stringify(errorFields)}`}</div>
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
  const { loginWithGoogle, loginWithPassword, register, logout } = useAuth()

  return (
    <>
      <button
        type="button"
        onClick={() => {
          void loginWithPassword('ana@exemplo.com', 'senha-forte-1')
        }}
      >
        entrar-senha
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
          void register('Ana', 'ana@exemplo.com', 'senha-forte-1')
        }}
      >
        cadastrar
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
  vi.mocked(authService.loginWithGoogle).mockResolvedValue(loginResultExpiringIn(60_000))
  vi.mocked(authService.loginWithPassword).mockResolvedValue(loginResultExpiringIn(60_000))
  vi.mocked(authService.register).mockResolvedValue(loginResultExpiringIn(60_000))
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

  it('loginWithPassword grava a sessão e autentica', async () => {
    const events = userEvent.setup()
    renderAuth('/login')

    await events.click(screen.getByRole('button', { name: 'entrar-senha' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('auth=true')
    })
    expect(vi.mocked(authService.loginWithPassword)).toHaveBeenCalledWith({
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
    })
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

  // US1 · FR-001/FR-010: o cadastro já inicia a sessão — mesma transição do login.
  it('register grava a sessão, autentica e devolve true', async () => {
    const events = userEvent.setup()
    renderAuth('/cadastro')

    await events.click(screen.getByRole('button', { name: 'cadastrar' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('auth=true')
    })
    expect(vi.mocked(authService.register)).toHaveBeenCalledWith({
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
    })

    const stored = localStorage.getItem(AUTH_STORAGE_KEY)
    expect(stored).toContain('jwt-novo')
    expect(JSON.parse(stored ?? '{}')).toMatchObject({
      token: 'jwt-novo',
      user: { id: 11, email: 'ana@exemplo.com' },
    })
    expect(screen.getByTestId('status')).toHaveTextContent('error=-')
  })

  it('register recusado em 409 devolve false, expõe a mensagem da API e não grava sessão', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.register).mockRejectedValueOnce(
      new ApiError(409, 'Este e-mail já está cadastrado.', []),
    )
    renderAuth('/cadastro')

    await events.click(screen.getByRole('button', { name: 'cadastrar' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('auth=false')
      expect(screen.getByTestId('status')).toHaveTextContent('error=Este e-mail já está cadastrado.')
      expect(screen.getByTestId('status')).toHaveTextContent('loading=false')
    })
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('register com 400 expõe os erros por campo vindos da API', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.register).mockRejectedValueOnce(
      new ApiError(400, 'Dados inválidos.', [{ campo: 'email', mensagem: 'E-mail inválido.' }]),
    )
    renderAuth('/cadastro')

    await events.click(screen.getByRole('button', { name: 'cadastrar' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('error=Dados inválidos.')
    })
    expect(screen.getByTestId('fields')).toHaveTextContent('fields={"email":"E-mail inválido."}')
  })

  // US2 · FR-002/FR-008 + research §3: o 401 anônimo de "credencial recusada" é
  // erro do formulário — não pode derrubar sessão nem virar "Sua sessão expirou".
  it('ApiError(401) do login devolve false, expõe a mensagem e NÃO faz logout(expirada)', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.loginWithPassword).mockRejectedValueOnce(
      new ApiError(401, 'E-mail ou senha inválidos', []),
    )
    renderAuth('/login')

    await events.click(screen.getByRole('button', { name: 'entrar-senha' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('auth=false')
      expect(screen.getByTestId('status')).toHaveTextContent('error=E-mail ou senha inválidos')
      expect(screen.getByTestId('status')).toHaveTextContent('loading=false')
    })

    // Nenhum logout('expirada') rodou: a rota não mudou nem ganhou o motivo.
    expect(locationText()).toBe('LOC:/login:null')
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('login recusado mantém a vendedora anônima, sem gravar sessão', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.loginWithPassword).mockRejectedValueOnce(new Error('credencial recusada'))
    renderAuth('/login')

    await events.click(screen.getByRole('button', { name: 'entrar-senha' }))

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('auth=false')
    })
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('clearError volta a não expor erro', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.loginWithPassword).mockRejectedValueOnce(new Error('credencial recusada'))
    renderAuth('/login')

    await events.click(screen.getByRole('button', { name: 'entrar-senha' }))
    await waitFor(() => {
      expect(screen.getByTestId('status')).not.toHaveTextContent('error=-')
    })

    await events.click(screen.getByRole('button', { name: 'limpar-erro' }))
    expect(screen.getByTestId('status')).toHaveTextContent('error=-')
  })

  it('clearError também limpa os erros de campo', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.register).mockRejectedValueOnce(
      new ApiError(400, 'Dados inválidos.', [{ campo: 'name', mensagem: 'O nome é obrigatório.' }]),
    )
    renderAuth('/cadastro')

    await events.click(screen.getByRole('button', { name: 'cadastrar' }))
    await waitFor(() => {
      expect(screen.getByTestId('fields')).toHaveTextContent('name')
    })

    await events.click(screen.getByRole('button', { name: 'limpar-erro' }))
    expect(screen.getByTestId('fields')).toHaveTextContent('fields={}')
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
