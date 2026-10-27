import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/Contexts/AuthContext'
import { ApiError } from '@/Services/apiClient'
import { authService } from '@/Services/authService'
import { AUTH_STORAGE_KEY, writeSession } from '@/Services/authStorage'
import { RegisterPage } from '@/pages/RegisterPage'

import type { AuthSession, LoginResult, UserInfo } from '@/types/auth'

/**
 * Só o serviço de autenticação é substituído: aqui se testa a TELA (validação, foco,
 * mensagens, navegação e o que ela manda para a API), não o transporte (apiClient)
 * nem a transição de sessão (AuthContext.test).
 */
vi.mock('@/Services/authService', () => ({
  authService: { register: vi.fn(), loginWithPassword: vi.fn(), loginWithGoogle: vi.fn() },
}))

const USER: UserInfo = {
  id: 11,
  email: 'ana@exemplo.com',
  name: 'Ana',
  provider: 'password',
  createdAt: '2026-01-01T00:00:00Z',
}

const loginResult: LoginResult = {
  token: 'jwt-novo',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  user: USER,
}

const sessionExpiringIn = (ms: number): AuthSession => ({
  token: 'jwt',
  expiresAt: new Date(Date.now() + ms).toISOString(),
  user: USER,
})

/** Sonda de destino: revela para onde a navegação foi e com que state. */
const LocationProbe = () => {
  const location = useLocation()

  return (
    <div data-testid="after">
      {`AT:${location.pathname}${location.search}:${JSON.stringify(location.state ?? null)}`}
    </div>
  )
}

const renderRegister = (entry = '/cadastro', state: unknown = null) =>
  render(
    <MemoryRouter
      initialEntries={[
        { pathname: entry, search: '', state },
      ]}
    >
      <AuthProvider>
        <Routes>
          <Route path="/cadastro" element={<RegisterPage />} />
          <Route path="/login" element={<LocationProbe />} />
          <Route path="/clientes" element={<LocationProbe />} />
          <Route path="/pedidos" element={<LocationProbe />} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )

/** Preenche o formulário inteiro (o helper deixa os campos individuais para os casos de erro). */
const fillAll = async (
  events: ReturnType<typeof userEvent.setup>,
  values: { name?: string; email?: string; password?: string; confirmPassword?: string },
) => {
  if (values.name !== undefined) {
    await events.type(screen.getByLabelText('Nome'), values.name)
  }

  if (values.email !== undefined) {
    await events.type(screen.getByLabelText('E-mail'), values.email)
  }

  if (values.password !== undefined) {
    await events.type(screen.getByLabelText('Senha'), values.password)
  }

  if (values.confirmPassword !== undefined) {
    await events.type(screen.getByLabelText('Confirmar senha'), values.confirmPassword)
  }
}

beforeEach(() => {
  localStorage.clear()
  vi.mocked(authService.register).mockResolvedValue(loginResult)
})

afterEach(() => {
  localStorage.clear()
})

describe('RegisterPage (US1)', () => {
  it('renderiza título, subtítulo e os quatro campos', () => {
    renderRegister()

    expect(screen.getByRole('heading', { name: 'Criar conta' })).toBeInTheDocument()
    expect(
      screen.getByText('Use seu e-mail e uma senha para acessar o Ambrosio.'),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Nome')).toBeInTheDocument()
    expect(screen.getByLabelText('E-mail')).toBeInTheDocument()
    expect(screen.getByLabelText('Senha')).toBeInTheDocument()
    expect(screen.getByLabelText('Confirmar senha')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Já tenho conta' })).toBeInTheDocument()
  })

  // US1 cenário 4 + FR-003: nada é enviado enquanto a senha não tem 8..128 caracteres.
  it('senha curta mostra a mensagem no campo e NÃO chama a API', async () => {
    const events = userEvent.setup()
    renderRegister()

    await fillAll(events, {
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: '1234567',
      confirmPassword: '1234567',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    const field = screen.getByLabelText('Senha')

    expect(screen.getByText('A senha deve ter entre 8 e 128 caracteres')).toBeInTheDocument()
    expect(field).toHaveAttribute('aria-invalid', 'true')
    expect(field.getAttribute('aria-describedby')).toMatch(/password/)
    expect(vi.mocked(authService.register)).not.toHaveBeenCalled()
  })

  it('confirmação diferente mostra "As senhas não conferem" e não envia', async () => {
    const events = userEvent.setup()
    renderRegister()

    await fillAll(events, {
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-2',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    expect(screen.getByText('As senhas não conferem')).toBeInTheDocument()
    expect(screen.getByLabelText('Confirmar senha')).toHaveAttribute('aria-invalid', 'true')
    expect(vi.mocked(authService.register)).not.toHaveBeenCalled()
  })

  it('e-mail mal formado mostra a mensagem no campo e não envia', async () => {
    const events = userEvent.setup()
    renderRegister()

    await fillAll(events, { name: 'Ana', email: 'ana@', password: 'senha-forte-1' })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    expect(screen.getByText('E-mail inválido')).toBeInTheDocument()
    expect(vi.mocked(authService.register)).not.toHaveBeenCalled()
  })

  it('nome vazio mostra "Informe seu nome" e foco vai para o primeiro campo inválido', async () => {
    const events = userEvent.setup()
    renderRegister()

    await fillAll(events, {
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-1',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    expect(screen.getByText('Informe seu nome')).toBeInTheDocument()
    expect(screen.getByLabelText('Nome')).toHaveFocus()
    expect(vi.mocked(authService.register)).not.toHaveBeenCalled()
  })

  // US1 cenário 1/7 + FR-011: 201 já inicia a sessão e a tela vai para o destino.
  it('envio válido cadastra com nome/e-mail cortados e a senha como digitada, e navega para o returnTo', async () => {
    const events = userEvent.setup()
    renderRegister('/cadastro', { returnTo: '/pedidos?tipo=sale' })

    await fillAll(events, {
      name: '  Ana Cadastro  ',
      email: '  Ana@Exemplo.COM ',
      // A senha vai exatamente como digitada (edge case: espaços são parte da senha).
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-1',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    await waitFor(() => {
      expect(screen.getByTestId('after')).toHaveTextContent('AT:/pedidos?tipo=sale')
    })
    expect(vi.mocked(authService.register)).toHaveBeenCalledWith({
      name: 'Ana Cadastro',
      email: 'Ana@Exemplo.COM',
      password: 'senha-forte-1',
    })
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toContain('jwt-novo')
  })

  it('sem returnTo navega para /clientes', async () => {
    const events = userEvent.setup()
    renderRegister()

    await fillAll(events, {
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-1',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    await waitFor(() => {
      expect(screen.getByTestId('after')).toHaveTextContent('AT:/clientes')
    })
  })

  // FR-006 + edge case "já existe": a mensagem da API é o erro geral, e as senhas ficam.
  it('409 mostra a mensagem da API em role="alert" e mantém as senhas nos campos', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.register).mockRejectedValueOnce(
      new ApiError(409, 'Este e-mail já está cadastrado.', []),
    )
    renderRegister()

    await fillAll(events, {
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-1',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Este e-mail já está cadastrado.')
    expect(screen.getByLabelText('Senha')).toHaveValue('senha-forte-1')
    expect(screen.getByLabelText('Confirmar senha')).toHaveValue('senha-forte-1')
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  it('409 de conta Google orienta a entrar com Google', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.register).mockRejectedValueOnce(
      new ApiError(409, 'Este e-mail já está cadastrado. Entre com sua conta Google.', []),
    )
    renderRegister()

    await fillAll(events, {
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-1',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Entre com sua conta Google.')
  })

  // FR-003: erro de campo da API volta ligado ao campo (400 com erros[].campo).
  it('400 com erros[].campo = "email" mostra a mensagem da API no campo e-mail', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.register).mockRejectedValueOnce(
      new ApiError(400, 'Dados inválidos.', [{ campo: 'email', mensagem: 'E-mail inválido.' }]),
    )
    renderRegister()

    await fillAll(events, {
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-1',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    await waitFor(() => {
      expect(screen.getByText('E-mail inválido.')).toBeInTheDocument()
    })
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('aria-invalid', 'true')
  })

  it('erro de rede mostra a mensagem de conexão', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.register).mockRejectedValueOnce(new Error('offline'))
    renderRegister()

    await fillAll(events, {
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-1',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('editar um campo limpa o erro geral', async () => {
    const events = userEvent.setup()
    vi.mocked(authService.register).mockRejectedValueOnce(
      new ApiError(409, 'Este e-mail já está cadastrado.', []),
    )
    renderRegister()

    await fillAll(events, {
      name: 'Ana',
      email: 'ana@exemplo.com',
      password: 'senha-forte-1',
      confirmPassword: 'senha-forte-1',
    })
    await events.click(screen.getByRole('button', { name: 'Criar conta' }))
    await screen.findByRole('alert')

    await events.type(screen.getByLabelText('Nome'), ' ')

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('com sessão ativa redireciona para /clientes', () => {
    writeSession(sessionExpiringIn(60_000))
    renderRegister()

    expect(screen.getByTestId('after')).toHaveTextContent('AT:/clientes')
  })

  it('link "Já tenho conta" leva a /login preservando o returnTo', async () => {
    const events = userEvent.setup()
    renderRegister('/cadastro', { returnTo: '/pedidos?tipo=sale' })

    await events.click(screen.getByRole('link', { name: 'Já tenho conta' }))

    expect(await screen.findByTestId('after')).toHaveTextContent(
      'AT:/login:{"returnTo":"/pedidos?tipo=sale"}',
    )
  })

  it('o botão Mostrar/Ocultar está presente nas duas senhas', () => {
    renderRegister()

    expect(screen.getAllByRole('button', { name: 'Mostrar senha' })).toHaveLength(2)
  })
})
