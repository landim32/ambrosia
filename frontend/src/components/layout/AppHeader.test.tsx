import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AppHeader } from '@/components/layout/AppHeader'
import { AuthProvider } from '@/Contexts/AuthContext'
import { writeSession } from '@/Services/authStorage'

import type { UserInfo } from '@/types/auth'

vi.mock('@/Services/authService', () => ({
  authService: { loginWithGoogle: vi.fn(), loginWithPassword: vi.fn(), register: vi.fn() },
}))

const USER: UserInfo = {
  id: 11,
  email: 'ana@exemplo.com',
  name: 'Ana Souza',
  provider: 'password',
  createdAt: '2026-01-01T00:00:00Z',
}

const renderHeader = (path = '/pedidos') =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<div>LOGIN</div>} />
          <Route
            path="*"
            element={
              <>
                <AppHeader />
                <Routes>
                  <Route path="/configuracoes/mais-vendas" element={<div>CONFIGURACOES</div>} />
                  <Route path="*" element={null} />
                </Routes>
              </>
            }
          />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )

/**
 * Barra superior (desktop): as seções principais em pílulas, sem
 * "Configurações", que fica dentro do menu "Conta" junto com o "Sair".
 */
describe('AppHeader', () => {
  beforeEach(() => {
    writeSession({
      token: 'jwt',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      user: USER,
    })
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('não mostra Configurações entre as seções principais', () => {
    renderHeader()

    const nav = screen.getByRole('navigation', { name: 'Navegação principal' })
    expect(within(nav).getByRole('link', { name: 'Clientes' })).toBeInTheDocument()
    expect(within(nav).queryByRole('link', { name: 'Configurações' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Configurações' })).not.toBeInTheDocument()
  })

  it('"Conta" abre o menu com Configurações e Sair; Esc fecha', async () => {
    const events = userEvent.setup()
    renderHeader()

    const account = screen.getByRole('button', { name: 'Conta' })
    expect(account).toHaveAttribute('aria-expanded', 'false')
    expect(account).toHaveTextContent('Ana Souza')

    await events.click(account)

    expect(account).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('link', { name: 'Configurações' })).toHaveAttribute(
      'href',
      '/configuracoes/mais-vendas',
    )
    expect(screen.getByRole('button', { name: 'Sair da sessão atual' })).toBeInTheDocument()

    await events.keyboard('{Escape}')

    expect(screen.queryByRole('link', { name: 'Configurações' })).not.toBeInTheDocument()
  })

  it('"Configurações" navega e fecha o menu', async () => {
    const events = userEvent.setup()
    renderHeader()

    await events.click(screen.getByRole('button', { name: 'Conta' }))
    await events.click(screen.getByRole('link', { name: 'Configurações' }))

    expect(screen.getByText('CONFIGURACOES')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Conta' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('"Sair" no menu encerra a sessão', async () => {
    const events = userEvent.setup()
    renderHeader()

    await events.click(screen.getByRole('button', { name: 'Conta' }))
    await events.click(screen.getByRole('button', { name: 'Sair da sessão atual' }))

    expect(localStorage.length).toBe(0)
  })
})
