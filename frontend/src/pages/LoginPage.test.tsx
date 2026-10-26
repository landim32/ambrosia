import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AUTH_STORAGE_KEY, writeSession } from '@/Services/authStorage'

import type { AuthSession, LoginResult, UserInfo } from '@/types/auth'

/**
 * Instancias estaveis: `vi.hoisted` permite usa-las nas fabricas de mock (o
 * Vitest as ia para o topo) E depois de `vi.resetModules()`, que reavalia o
 * escopo do modulo de `LoginPage`.
 */
const mocks = vi.hoisted(() => ({
  authService: { loginWithGoogle: vi.fn(), loginDev: vi.fn() },
  useGoogleIdentity: vi.fn(),
}))

vi.mock('@/Services/authService', () => ({ authService: mocks.authService }))

// O script do Google nunca entra no jsdom: o hook e substituido pelo seu efeito.
vi.mock('@/hooks/useGoogleIdentity', () => ({
  useGoogleIdentity: mocks.useGoogleIdentity,
  default: mocks.useGoogleIdentity,
}))

const USER: UserInfo = {
  id: 11,
  email: 'ana@exemplo.com',
  name: 'Ana',
  provider: 'dev',
  createdAt: '2026-01-01T00:00:00Z',
}

const sessionExpiringIn = (ms: number): AuthSession => ({
  token: 'jwt',
  expiresAt: new Date(Date.now() + ms).toISOString(),
  user: USER,
})

const loginResult: LoginResult = {
  token: 'jwt-novo',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  user: USER,
}

const AfterLogin = () => {
  const location = useLocation()

  return <div data-testid="after">{`AT:${location.pathname}${location.search}`}</div>
}

/**
 * Codigo da aplicacao importado DO ZERO. A flag `VITE_DEV_LOGIN` e lida no
 * ESCOPO DO MODULO (research §8 / T044) — assim o Rollup elimina o chunk em
 * producao —, entao so um reimport enxerga o `vi.stubEnv` do teste. provider e
 * pagina vem da MESMA copia do grafo: `node_modules` e compartilhado, `src/` nao.
 */
const importGraph = async () => {
  vi.resetModules()

  const [{ AuthProvider }, page] = await Promise.all([
    import('@/Contexts/AuthContext'),
    import('@/pages/LoginPage'),
  ])

  return { AuthProvider, LoginPage: page.LoginPage }
}

/** provider + pagina vindos do mesmo grafo reimportado. */
type Graph = Awaited<ReturnType<typeof importGraph>>

/** Monta /login com pathname e search separados (MemoryRouter casa a rota). */
const renderLogin = (
  { AuthProvider, LoginPage }: Graph,
  entry = '/login',
  state: unknown = null,
) => {
  const [pathname, search] = entry.split('?')

  return render(
    <MemoryRouter
      initialEntries={[
        { pathname: pathname ?? '/login', search: search === undefined ? '' : `?${search}`, state },
      ]}
    >
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/clientes" element={<AfterLogin />} />
          <Route path="/pedidos" element={<AfterLogin />} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  localStorage.clear()
  mocks.useGoogleIdentity.mockImplementation(() => undefined)
  mocks.authService.loginDev.mockResolvedValue(loginResult)
  mocks.authService.loginWithGoogle.mockResolvedValue(loginResult)
})

afterEach(() => {
  vi.unstubAllEnvs()
  localStorage.clear()
})

describe('LoginPage', () => {
  it('mostra o formulario de dev quando VITE_DEV_LOGIN=true', async () => {
    vi.stubEnv('VITE_DEV_LOGIN', 'true')
    renderLogin(await importGraph())

    expect(await screen.findByRole('textbox', { name: /E-mail/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('nao mostra o formulario de dev quando a flag esta ausente (producao)', async () => {
    vi.stubEnv('VITE_DEV_LOGIN', undefined)
    renderLogin(await importGraph())

    await screen.findByRole('heading', { name: /Ambrosia/i })
    expect(screen.queryByRole('textbox', { name: /E-mail/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Entrar' })).not.toBeInTheDocument()
  })

  it('com ?motivo=expirada exibe o aviso de sessao expirada como alerta', async () => {
    vi.stubEnv('VITE_DEV_LOGIN', undefined)
    renderLogin(await importGraph(), '/login?motivo=expirada')

    expect(screen.getByRole('alert')).toHaveTextContent('Sua sessão expirou')
  })

  it('sem o parametro nao ha aviso de expiracao', async () => {
    vi.stubEnv('VITE_DEV_LOGIN', undefined)
    renderLogin(await importGraph())

    expect(await screen.findByRole('heading', { name: /Ambrosia/i })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('logada, redireciona para /clientes', async () => {
    writeSession(sessionExpiringIn(60_000))
    renderLogin(await importGraph())

    expect(screen.getByTestId('after')).toHaveTextContent('AT:/clientes')
  })

  it('logada, respeita o returnTo da guarda', async () => {
    writeSession(sessionExpiringIn(60_000))
    renderLogin(await importGraph(), '/login', { returnTo: '/pedidos?tipo=sale' })

    expect(screen.getByTestId('after')).toHaveTextContent('AT:/pedidos?tipo=sale')
  })

  it('apos o login dev navega para o returnTo e grava a sessao', async () => {
    vi.stubEnv('VITE_DEV_LOGIN', 'true')
    const events = userEvent.setup()
    renderLogin(await importGraph(), '/login', { returnTo: '/pedidos?tipo=sale' })

    const email = await screen.findByRole('textbox', { name: /E-mail/i })
    const name = screen.getByRole('textbox', { name: /Nome/i })

    await events.clear(email)
    await events.type(email, 'ana@exemplo.com')
    await events.clear(name)
    await events.type(name, 'Ana')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => {
      expect(screen.getByTestId('after')).toHaveTextContent('AT:/pedidos?tipo=sale')
    })
    expect(mocks.authService.loginDev).toHaveBeenCalledWith('ana@exemplo.com', 'Ana')
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toContain('jwt-novo')
  })

  it('login dev recusado nao navega e mantem a tela de entrada', async () => {
    vi.stubEnv('VITE_DEV_LOGIN', 'true')
    mocks.authService.loginDev.mockRejectedValueOnce(new Error('indisponível'))
    const events = userEvent.setup()
    renderLogin(await importGraph())

    const email = await screen.findByRole('textbox', { name: /E-mail/i })
    await events.clear(email)
    await events.type(email, 'ana@exemplo.com')
    await events.clear(screen.getByRole('textbox', { name: /Nome/i }))
    await events.type(screen.getByRole('textbox', { name: /Nome/i }), 'Ana')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled()
    })
    expect(screen.queryByTestId('after')).not.toBeInTheDocument()
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('usa o credential do Google Identity para autenticar', async () => {
    let onCredential: ((credential: string) => void) | undefined

    mocks.useGoogleIdentity.mockImplementation((callback: (credential: string) => void) => {
      onCredential = callback

      return undefined
    })
    const events = userEvent.setup()
    renderLogin(await importGraph())

    expect(onCredential).toBeTypeOf('function')
    await events.click(screen.getByRole('main'))
    onCredential?.('id-token-do-google')

    await waitFor(() => {
      expect(screen.getByTestId('after')).toHaveTextContent('AT:/clientes')
    })
    expect(mocks.authService.loginWithGoogle).toHaveBeenCalledWith('id-token-do-google')
  })
})
