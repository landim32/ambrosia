import { cleanup, configure, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { App } from '@/App'
import { AuthProvider } from '@/Contexts/AuthContext'
import { CustomerProvider } from '@/Contexts/CustomerContext'
import { OrderProvider } from '@/Contexts/OrderContext'

import { AUTH_STORAGE_KEY } from '@/Services/authStorage'

import type { AuthSession } from '@/types/auth'

/**
 * Fumaça de integração (quickstart, Cenários 1–4 sem navegador).
 *
 * Monta a MESMA cadeia de `main.tsx` (roteador → Auth → Customer → Order → App)
 * com uma sessão válida no storage e um `fetch` falso que devolve a página pedida
 * pela API. Não repete a UI de cada tela — isso mora nos testes de componente — e
 * sim que a arquitetura inteira se liga: o `AuthContext` aceita a sessão guardada,
 * `RequireAuth` deixa passar, `AppShell` desenha o cabeçalho com a identidade da
 * vendedora, a rota chama o serviço e o card sai do JSON camelCase da API.
 *
 * Um import quebrado, um provider fora da ordem ou um casing errado de caminho
 * (Princípio III) derruba este teste antes de derrubar o build do Docker.
 */

/**
 * As rotas montam sob `React.lazy`: a página só aparece quando o chunk resolve,
 * e sob a suíte inteira (19 arquivos) isso passa do prazo padrão de 1 s do RTL.
 * O prazo maior é a única concessão de tempo daqui — nada de esconder o Suspense.
 */
configure({ asyncUtilTimeout: 5000 })

const session: AuthSession = {
  token: 'jwt-falso',
  expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
  user: {
    id: 9,
    email: 'ana@exemplo.com',
    name: 'Ana',
    provider: 'dev',
    createdAt: '2026-01-01T00:00:00Z',
  },
}

const customersPage = {
  items: [
    {
      id: 42,
      name: 'Maria da Silva',
      cpfMasked: '***.444.777-**',
      createdAt: '2026-03-01T10:00:00Z',
      updatedAt: '2026-03-01T10:00:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
}

const ordersPage = {
  items: [
    {
      id: 26,
      customerId: 42,
      customerName: 'Maria da Silva',
      orderNumber: '00004751',
      orderDate: '2026-03-01',
      kind: 'sale',
      channel: 'Linx',
      origin: 'cota',
      status: 'Faturado',
      total: 1234.56,
      itemsCount: 2,
      createdAt: '2026-03-01T10:00:00Z',
      updatedAt: '2026-03-01T10:00:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
}

/** `fetch` falso: devolve a página certa conforme o caminho pedido. */
const mockFetch = vi.fn((input: unknown) => {
  const url = String(input)

  return Promise.resolve(
    new Response(JSON.stringify(url.includes('/orders') ? ordersPage : customersPage), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  )
})

const renderApp = (path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <CustomerProvider>
          <OrderProvider>
            <App />
          </OrderProvider>
        </CustomerProvider>
      </AuthProvider>
    </MemoryRouter>,
  )

beforeEach(() => {
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session))
  vi.stubGlobal('fetch', mockFetch)
})

afterEach(() => {
  cleanup()
})

describe('integração: cadeia de providers com sessão válida', () => {
  it('/clientes renderiza o shell e o resultado da API', async () => {
    renderApp('/clientes')

    // Cabeçalho (FR-005/FR-006) vem da SESSÃO guardada, sem chamar /users/me.
    expect(await screen.findByText('Ana')).toBeInTheDocument()
    expect(screen.getByText('ana@exemplo.com')).toBeInTheDocument()

    // A lista saiu do JSON camelCase da API (FR-008), com CPF mascarado (FR-022).
    expect(await screen.findByText('Maria da Silva')).toBeInTheDocument()
    expect(screen.getByText('***.444.777-**')).toBeInTheDocument()
  })

  it('chama a API com Authorization Bearer e a query de página', async () => {
    renderApp('/clientes')

    await screen.findByText('Maria da Silva')

    const [url, init] = mockFetch.mock.calls[0] as unknown as [string, RequestInit]

    expect(url).toContain('/customers')
    expect(url).toContain('page=1')
    expect(url).toContain('pageSize=20')
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer jwt-falso')
  })

  it('/pedidos leva os filtros da URL à API, sem data padrão (FR-014/FR-015)', async () => {
    renderApp('/pedidos?tipo=sale&canal=lin')

    // O que se prova aqui é a CONSULTA; a renderização do card já é coberta em
    // OrderCard/OrdersPage.
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalled()
    })

    const [url] = mockFetch.mock.calls[0] as unknown as [string]

    expect(url).toContain('kind=sale')
    expect(url).toContain('channel=lin')
    expect(url).toContain('pageSize=20')
    expect(url).not.toContain('from=')
    expect(url).not.toContain('to=')
  })

  it('sem sessão cai em /login e não chama a API (FR-001)', async () => {
    localStorage.clear()
    renderApp('/clientes')

    expect(await screen.findByRole('heading', { name: /Ambrosia/i })).toBeInTheDocument()
    expect(mockFetch).not.toHaveBeenCalled()
  })
})
