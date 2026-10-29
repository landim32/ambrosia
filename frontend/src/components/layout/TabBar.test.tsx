import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { TabBar } from '@/components/layout/TabBar'
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

const renderTabBar = (path = '/pedidos') =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<div>LOGIN</div>} />
          <Route path="*" element={<TabBar />} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )

/**
 * Tab bar do rodapé (layout mobile estilo iOS): as três seções com rótulo e o
 * item "Conta", que abre a folha com a identidade da vendedora, "Configurações"
 * e o "Sair".
 */
describe('TabBar', () => {
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

  it('mostra Clientes, Pedidos e Importar com o item da rota atual marcado', () => {
    renderTabBar('/pedidos')

    const nav = screen.getByRole('navigation', { name: 'Navegação principal' })
    expect(within(nav).getByRole('link', { name: 'Clientes' })).toHaveAttribute('href', '/clientes')
    expect(within(nav).getByRole('link', { name: 'Importar' })).toHaveAttribute('href', '/importacao')
    expect(within(nav).getByRole('link', { name: 'Pedidos' })).toHaveAttribute('aria-current', 'page')
    expect(within(nav).queryByRole('link', { name: 'Configurações' })).not.toBeInTheDocument()
  })

  it('"Conta" abre a folha com nome, e-mail e Sair; Esc fecha', async () => {
    const events = userEvent.setup()
    renderTabBar()

    const account = screen.getByRole('button', { name: 'Conta' })
    expect(account).toHaveAttribute('aria-expanded', 'false')

    await events.click(account)

    const sheet = screen.getByRole('dialog', { name: 'Sua conta' })
    expect(within(sheet).getByText('Ana Souza')).toBeInTheDocument()
    expect(within(sheet).getByText('ana@exemplo.com')).toBeInTheDocument()
    expect(within(sheet).getByRole('link', { name: 'Configurações' })).toHaveAttribute(
      'href',
      '/configuracoes/mais-vendas',
    )
    expect(within(sheet).getByRole('button', { name: 'Sair da sessão atual' })).toBeInTheDocument()
    expect(account).toHaveAttribute('aria-expanded', 'true')

    await events.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('"Sair" na folha encerra a sessão', async () => {
    const events = userEvent.setup()
    renderTabBar()

    await events.click(screen.getByRole('button', { name: 'Conta' }))
    await events.click(screen.getByRole('button', { name: 'Sair da sessão atual' }))

    expect(localStorage.length).toBe(0)
  })

  it('"Configurações" na folha navega e fecha a folha', async () => {
    const events = userEvent.setup()
    render(
      <MemoryRouter initialEntries={['/pedidos']}>
        <AuthProvider>
          <Routes>
            <Route
              path="*"
              element={
                <>
                  <Routes>
                    <Route path="/configuracoes/mais-vendas" element={<div>CONFIGURACOES</div>} />
                    <Route path="*" element={null} />
                  </Routes>
                  <TabBar />
                </>
              }
            />
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    )

    await events.click(screen.getByRole('button', { name: 'Conta' }))
    await events.click(screen.getByRole('link', { name: 'Configurações' }))

    expect(screen.getByText('CONFIGURACOES')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
