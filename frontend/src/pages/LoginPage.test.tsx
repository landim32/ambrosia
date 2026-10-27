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
  authService: {
    loginWithGoogle: vi.fn(),
    loginWithPassword: vi.fn(),
    register: vi.fn(),
  },
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
  provider: 'password',
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

/** Sonda de destino: revela para onde se foi, com que search e com que state. */
const AfterLogin = () => {
  const location = useLocation()

  return (
    <div data-testid="after">
      {`AT:${location.pathname}${location.search}:${JSON.stringify(location.state ?? null)}`}
    </div>
  )
}

/**
 * Codigo da aplicacao importado DO ZERO: o grafo e reavaliado a cada teste para
 * que o `vi.stubEnv` de `VITE_GOOGLE_CLIENT_ID` (lido no corpo do componente) e o
 * estado do AuthProvider comecem limpos.
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

/** Monta /login com pathname, search e state separados (MemoryRouter casa a rota). */
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
          <Route path="/cadastro" element={<AfterLogin />} />
          <Route path="/clientes" element={<AfterLogin />} />
          <Route path="/pedidos" element={<AfterLogin />} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}

/** O input por id: `type="password"` nao tem role de textbox. */
const inputOf = (id: string): HTMLInputElement => {
  const input = document.getElementById(id)

  if (!(input instanceof HTMLInputElement)) throw new Error(`campo ${id} nao encontrado`)

  return input
}

beforeEach(() => {
  localStorage.clear()
  mocks.useGoogleIdentity.mockImplementation(() => undefined)
  mocks.authService.loginWithPassword.mockResolvedValue(loginResult)
  mocks.authService.loginWithGoogle.mockResolvedValue(loginResult)
  mocks.authService.register.mockResolvedValue(loginResult)
})

afterEach(() => {
  vi.unstubAllEnvs()
  localStorage.clear()
})

describe('LoginPage', () => {
  it('tem campo de e-mail, campo de senha com mostrar/ocultar e botao Entrar', async () => {
    renderLogin(await importGraph())

    expect(await screen.findByRole('heading', { name: /Ambrosio/i })).toBeInTheDocument()
    expect(inputOf('login-email')).toBeInTheDocument()
    expect(inputOf('login-password')).toHaveAttribute('type', 'password')
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mostrar senha' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Criar conta' })).toBeInTheDocument()
  })

  /** Mascote do Ambrosio: compacto no mobile e grande no desktop, sempre decorativo. */
  it('mostra o mascote como imagem decorativa, fora da árvore de acessibilidade', async () => {
    renderLogin(await importGraph())

    await screen.findByRole('heading', { name: /Ambrosio/i })
    for (const testId of ['auth-mascot', 'auth-mascot-hero']) {
      const mascot = screen.getByTestId(testId)
      expect(mascot).toHaveAttribute('alt', '')
      expect(mascot).toHaveAttribute('aria-hidden', 'true')
    }
    // Decorativo: nenhuma imagem com nome acessível além das que já existiam.
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('envio valido chama loginWithPassword com o e-mail cortado e a senha como digitada', async () => {
    const events = userEvent.setup()
    renderLogin(await importGraph(), '/login', { returnTo: '/pedidos?tipo=sale' })

    await events.type(inputOf('login-email'), '  ana@exemplo.com  ')
    await events.type(inputOf('login-password'), 'senha-forte-1')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => {
      expect(mocks.authService.loginWithPassword).toHaveBeenCalledWith({
        email: 'ana@exemplo.com',
        password: 'senha-forte-1',
      })
    })
    await waitFor(() => {
      expect(screen.getByTestId('after')).toHaveTextContent('AT:/pedidos?tipo=sale')
    })
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toContain('jwt-novo')
  })

  it('sem returnTo o sucesso vai para /clientes', async () => {
    const events = userEvent.setup()
    renderLogin(await importGraph())

    await events.type(inputOf('login-email'), 'ana@exemplo.com')
    await events.type(inputOf('login-password'), 'senha-forte-1')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => {
      expect(screen.getByTestId('after')).toHaveTextContent('AT:/clientes')
    })
  })

  it('enviar vazio mostra a mensagem, foca o campo e nao chama a API', async () => {
    const events = userEvent.setup()
    renderLogin(await importGraph())

    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(inputOf('login-email')).toHaveFocus()
    expect(inputOf('login-email')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByText('Informe seu e-mail')).toBeInTheDocument()
    expect(mocks.authService.loginWithPassword).not.toHaveBeenCalled()
  })

  it('senha vazia pede a senha e nao envia', async () => {
    const events = userEvent.setup()
    renderLogin(await importGraph())

    await events.type(inputOf('login-email'), 'ana@exemplo.com')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(screen.getByText('Informe sua senha')).toBeInTheDocument()
    expect(inputOf('login-password')).toHaveFocus()
    expect(mocks.authService.loginWithPassword).not.toHaveBeenCalled()
  })

  it('e-mail mal formado mostra "E-mail inválido" e nao envia', async () => {
    const events = userEvent.setup()
    renderLogin(await importGraph())

    await events.type(inputOf('login-email'), 'ana@')
    await events.type(inputOf('login-password'), 'senha-forte-1')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('E-mail inválido')).toBeInTheDocument()
    expect(mocks.authService.loginWithPassword).not.toHaveBeenCalled()
  })

  it('401 mostra a mensagem da API em role="alert" e a senha continua no campo', async () => {
    const events = userEvent.setup()
    mocks.authService.loginWithPassword.mockRejectedValueOnce(new Error('E-mail ou senha inválidos'))
    renderLogin(await importGraph())

    await events.type(inputOf('login-email'), 'ana@exemplo.com')
    await events.type(inputOf('login-password'), 'senha-errada')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('E-mail ou senha inválidos')
    expect(inputOf('login-password')).toHaveValue('senha-errada')
    expect(screen.queryByTestId('after')).not.toBeInTheDocument()
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('o botao Mostrar/Ocultar alterna o tipo do campo de senha', async () => {
    const events = userEvent.setup()
    renderLogin(await importGraph())

    await events.click(screen.getByRole('button', { name: 'Mostrar senha' }))

    expect(inputOf('login-password')).toHaveAttribute('type', 'text')
    expect(screen.getByRole('button', { name: 'Ocultar senha' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('?motivo=expirada mostra o aviso de sessao expirada', async () => {
    renderLogin(await importGraph(), '/login?motivo=expirada')

    expect(await screen.findByRole('alert')).toHaveTextContent('Sua sessão expirou')
  })

  it('sem o parametro nao ha aviso de expiracao', async () => {
    renderLogin(await importGraph())

    await screen.findByRole('heading', { name: /Ambrosio/i })
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('link "Criar conta" leva a /cadastro preservando o returnTo', async () => {
    const events = userEvent.setup()
    renderLogin(await importGraph(), '/login', { returnTo: '/pedidos?tipo=sale' })

    await events.click(screen.getByRole('link', { name: 'Criar conta' }))

    expect(await screen.findByTestId('after')).toHaveTextContent(
      'AT:/cadastro:{"returnTo":"/pedidos?tipo=sale"}',
    )
  })

  it('sem VITE_GOOGLE_CLIENT_ID nao ha separador "ou" nem botao Google', async () => {
    vi.stubEnv('VITE_GOOGLE_CLIENT_ID', undefined)
    renderLogin(await importGraph())

    await screen.findByRole('heading', { name: /Ambrosio/i })
    expect(screen.queryByText('ou')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Entrar com Google' })).not.toBeInTheDocument()
  })

  it('com VITE_GOOGLE_CLIENT_ID o botao do Google aparece acima do formulario, com o "ou"', async () => {
    vi.stubEnv('VITE_GOOGLE_CLIENT_ID', 'client-id-de-teste')
    const { container } = renderLogin(await importGraph())

    const google = await screen.findByTestId('google-button')
    const email = inputOf('login-email')

    expect(screen.getByText('ou')).toBeInTheDocument()
    // O formulario inteiro vem depois do container do Google.
    expect(google.compareDocumentPosition(email) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(container.querySelector('form')).toBeInTheDocument()
  })

  it('usa o credential do Google Identity para autenticar', async () => {
    let onCredential: ((credential: string) => void) | undefined

    mocks.useGoogleIdentity.mockImplementation((callback: (credential: string) => void) => {
      onCredential = callback

      return undefined
    })
    renderLogin(await importGraph())
    await screen.findByRole('heading', { name: /Ambrosio/i })

    expect(onCredential).toBeTypeOf('function')
    onCredential?.('id-token-do-google')

    await waitFor(() => {
      expect(screen.getByTestId('after')).toHaveTextContent('AT:/clientes')
    })
    expect(mocks.authService.loginWithGoogle).toHaveBeenCalledWith('id-token-do-google')
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

  it('editar um campo limpa o erro geral', async () => {
    const events = userEvent.setup()
    mocks.authService.loginWithPassword.mockRejectedValueOnce(new Error('E-mail ou senha inválidos'))
    renderLogin(await importGraph())

    await events.type(inputOf('login-email'), 'ana@exemplo.com')
    await events.type(inputOf('login-password'), 'senha-errada')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))
    await screen.findByRole('alert')

    await events.type(inputOf('login-password'), 'x')

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('a senha digitada continua no campo depois de um erro de formato', async () => {
    const events = userEvent.setup()
    renderLogin(await importGraph())

    await events.type(inputOf('login-email'), 'ana@')
    await events.type(inputOf('login-password'), 'senha-forte-1')
    await events.click(screen.getByRole('button', { name: 'Entrar' }))

    await screen.findByText('E-mail inválido')
    expect(inputOf('login-password')).toHaveValue('senha-forte-1')
  })
})
