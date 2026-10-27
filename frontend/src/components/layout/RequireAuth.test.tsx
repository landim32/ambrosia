import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { RequireAuth } from '@/components/layout/RequireAuth'
import { AuthProvider } from '@/Contexts/AuthContext'
import { writeSession } from '@/Services/authStorage'

import type { AuthSession, UserInfo } from '@/types/auth'

vi.mock('@/Services/authService', () => ({
  authService: { loginWithGoogle: vi.fn(), loginWithPassword: vi.fn(), register: vi.fn() },
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

/** Revele o destino do redirecionamento (pathname + search + state.returnTo). */
const LoginProbe = () => {
  const location = useLocation()

  return (
    <div data-testid="login-screen">
      {`LOGIN:${location.pathname}${location.search}:${JSON.stringify(location.state ?? null)}`}
    </div>
  )
}

const renderGuarded = (initialPath: string) =>
  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginProbe />} />
          <Route element={<RequireAuth />}>
            <Route path="/pedidos" element={<div>PEDIDOS</div>} />
            <Route path="/clientes/:id" element={<div>PERFIL</div>} />
          </Route>
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )

afterEach(() => {
  localStorage.clear()
})

describe('RequireAuth', () => {
  it('sem sessão redireciona para /login guardando path + query em state.returnTo', () => {
    renderGuarded('/pedidos?tipo=sale')

    expect(screen.queryByText('PEDIDOS')).not.toBeInTheDocument()
    expect(screen.getByTestId('login-screen')).toHaveTextContent(
      'LOGIN:/login:{"returnTo":"/pedidos?tipo=sale"}',
    )
  })

  it('sem sessão e sem query, returnTo é só o pathname', () => {
    renderGuarded('/clientes/42')

    expect(screen.getByTestId('login-screen')).toHaveTextContent(
      'LOGIN:/login:{"returnTo":"/clientes/42"}',
    )
  })

  it('com sessão válida renderiza a rota protegida', () => {
    writeSession(sessionExpiringIn(60_000))
    renderGuarded('/pedidos?tipo=sale')

    expect(screen.getByText('PEDIDOS')).toBeInTheDocument()
    expect(screen.queryByTestId('login-screen')).not.toBeInTheDocument()
  })

  it('sessão vencida no storage não chega a renderizar a rota protegida', () => {
    writeSession(sessionExpiringIn(-1_000))
    renderGuarded('/pedidos?tipo=sale')

    expect(screen.queryByText('PEDIDOS')).not.toBeInTheDocument()
  })
})
