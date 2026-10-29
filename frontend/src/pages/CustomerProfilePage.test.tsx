import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CustomerPhotoProvider } from '@/Contexts/CustomerPhotoContext'
import { CustomerProvider } from '@/Contexts/CustomerContext'
import { CustomerProfilePage } from '@/pages/CustomerProfilePage'

import type { ReactNode } from 'react'
import type { CustomerDetailInfo } from '@/types/customer'
import type { CustomerPhotoInfo } from '@/types/customerPhoto'
import type { OrderInfo } from '@/types/order'

/**
 * Perfil da cliente com as abas da feature 009 (T035, contracts/ui.md §1):
 * Fotos é a padrão, `?aba=pedidos` abre a outra, e o círculo do cabeçalho só é
 * botão quando existe foto de perfil (FR-014, US3-4/US3-5).
 *
 * Os serviços são substituídos: o que se exercita é a tela e o `Tabs`, não o
 * transporte (que já tem suíte própria).
 */
const mocks = vi.hoisted(() => ({
  customerService: { list: vi.fn(), getById: vi.fn() },
  orderService: { list: vi.fn(), getById: vi.fn() },
  customerPhotoService: {
    list: vi.fn(),
    attach: vi.fn(),
    setDescription: vi.fn(),
    favorite: vi.fn(),
    unfavorite: vi.fn(),
    remove: vi.fn(),
    setProfile: vi.fn(),
    removeProfile: vi.fn(),
  },
  imageService: { upload: vi.fn(), getReadUrl: vi.fn(), getById: vi.fn() },
}))

vi.mock('@/Services/customerService', () => ({ customerService: mocks.customerService }))
vi.mock('@/Services/orderService', () => ({ orderService: mocks.orderService }))
vi.mock('@/Services/customerPhotoService', () => ({
  customerPhotoService: mocks.customerPhotoService,
}))
vi.mock('@/Services/imageService', () => ({ imageService: mocks.imageService }))

const photo = (photoId: number, overrides: Partial<CustomerPhotoInfo> = {}): CustomerPhotoInfo => ({
  photoId,
  customerId: 88,
  imageId: photoId * 10,
  description: null,
  isFavorite: false,
  isProfile: false,
  source: 'web',
  createdAt: '2026-10-29T10:00:00Z',
  updatedAt: '2026-10-29T10:00:00Z',
  url: `https://storage.example/${String(photoId)}.jpg`,
  urlExpiresAt: '2099-01-01T00:00:00Z',
  ...overrides,
})

const detail = (overrides: Partial<CustomerDetailInfo> = {}): CustomerDetailInfo => ({
  id: 88,
  name: 'Maria Souza',
  cpfMasked: '***.982.247-**',
  createdAt: '2026-01-05T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
  cpf: '123.982.247-00',
  summary: { purchases: 12, netTotal: 3456.7, lastPurchase: '2026-10-01', daysSinceLastPurchase: 3 },
  photoCount: 1,
  ...overrides,
})

const order = (overrides: Partial<OrderInfo> = {}): OrderInfo => ({
  id: 4001,
  customerId: 88,
  customerName: 'Maria Souza',
  orderNumber: '00004001',
  orderDate: '2026-10-01',
  kind: 'sale',
  channel: 'Loja',
  origin: 'cota',
  status: 'Pago',
  total: 250,
  source: 'maisVendas',
  itemsCount: 1,
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
  ...overrides,
})

const PAGED_ORDERS = { items: [order()], page: 1, pageSize: 20, totalCount: 1 }

/**
 * Sonda da URL: um componente de verdade (e não uma função chamada no JSX do
 * wrapper), porque `useLocation` só existe dentro do contexto do `<Router>`.
 */
const LocationProbe = () => {
  const { search } = useLocation()

  return <div data-testid="location">{search}</div>
}

describe('CustomerProfilePage (abas e perfil)', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    mocks.customerService.getById.mockResolvedValue(detail())
    mocks.orderService.list.mockResolvedValue(PAGED_ORDERS)
    mocks.customerPhotoService.list.mockResolvedValue({
      items: [photo(1204)],
      nextCursor: null,
    })
    mocks.imageService.getReadUrl.mockResolvedValue(null)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  /** A tela montada dentro da rota real, com a URL que o teste pedir. */
  const page = (entry = '/clientes/88'): void => {
    const wrapper = ({ children }: { children: ReactNode }) => (
      <MemoryRouter initialEntries={[entry]}>
        <CustomerProvider>
          <CustomerPhotoProvider>
            <>
              {children}
              <LocationProbe />
            </>
          </CustomerPhotoProvider>
        </CustomerProvider>
      </MemoryRouter>
    )

    render(
      <Routes>
        <Route path="/clientes/:id" element={<CustomerProfilePage />} />
      </Routes>,
      { wrapper },
    )
  }

  it('Fotos é a aba padrão, sem parâmetro na URL (FR-017)', async () => {
    page()

    await waitFor(() => {
      expect(screen.getByTestId('photo-cells')).toBeInTheDocument()
    })

    expect(screen.getByTestId('tab-photos')).toHaveAttribute('data-state', 'active')
    expect(screen.getByTestId('tab-orders')).toHaveAttribute('data-state', 'inactive')
  })

  it('?aba=pedidos abre a lista de pedidos como sempre foi (US1-5)', async () => {
    page('/clientes/88?aba=pedidos')

    await waitFor(() => {
      expect(screen.getByText('00004001')).toBeInTheDocument()
    })

    expect(screen.getByTestId('tab-orders')).toHaveAttribute('data-state', 'active')
    expect(screen.queryByTestId('photo-cells')).not.toBeInTheDocument()
  })

  it('clicar em Pedidos escreve ?aba=pedidos na URL', async () => {
    const user = userEvent.setup()
    page()

    await waitFor(() => {
      expect(screen.getByTestId('tab-photos')).toBeInTheDocument()
    })

    await user.click(screen.getByTestId('tab-orders'))

    expect(screen.getByTestId('tab-orders')).toHaveAttribute('data-state', 'active')
    expect(screen.getByTestId('location')).toHaveTextContent('?aba=pedidos')
  })

  it('voltar para Fotos limpa o parâmetro da URL', async () => {
    const user = userEvent.setup()
    page('/clientes/88?aba=pedidos')

    await waitFor(() => {
      expect(screen.getByTestId('tab-orders')).toHaveAttribute('data-state', 'active')
    })

    await user.click(screen.getByTestId('tab-photos'))

    expect(screen.getByTestId('location')).toHaveTextContent('')
  })

  it('carrega o álbum da cliente aberta pelo contexto', async () => {
    page()

    await waitFor(() => {
      expect(mocks.customerPhotoService.list).toHaveBeenCalledWith({
        customerId: 88,
        cursor: null,
        favorites: false,
      })
    })
  })

  /** US3-5: sem foto de perfil, o círculo são as iniciais e nada é clicável. */
  it('sem perfil o avatar não é botão', async () => {
    mocks.customerService.getById.mockResolvedValue(detail({ profilePhoto: null }))

    page()

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Maria Souza' })).toBeInTheDocument()
    })

    expect(screen.queryByTestId('profile-avatar-button')).not.toBeInTheDocument()
    expect(screen.queryByTestId('adjust-profile')).not.toBeInTheDocument()
  })

  /** FR-014 / US3-4: tocar no círculo abre a ORIGINAL da foto de origem. */
  it('com perfil o avatar é botão e abre a foto original', async () => {
    const user = userEvent.setup()

    mocks.customerService.getById.mockResolvedValue(
      detail({
        profilePhoto: {
          photoId: 1204,
          imageId: 5740,
          originalImageId: 12040,
          url: 'https://storage.example/5740.webp',
          originalUrl: 'https://storage.example/1204.jpg',
          urlExpiresAt: '2099-01-01T00:00:00Z',
        },
      }),
    )

    page()

    await waitFor(() => {
      expect(screen.getByTestId('profile-avatar-button')).toBeInTheDocument()
    })

    expect(screen.getByTestId('adjust-profile')).toBeInTheDocument()

    await user.click(screen.getByTestId('profile-avatar-button'))

    await waitFor(() => {
      expect(screen.getByTestId('viewer-image')).toHaveAttribute(
        'src',
        'https://storage.example/1204.jpg',
      )
    })
  })

  /**
   * O círculo abre mesmo quando a foto do perfil está além das páginas carregadas
   * (não existe rota avulsa de foto — research R10 —, então a célula vem do
   * próprio `profilePhoto`).
   */
  it('abre o perfil mesmo com a foto fora da página carregada', async () => {
    const user = userEvent.setup()

    mocks.customerPhotoService.list.mockResolvedValue({ items: [photo(9999)], nextCursor: null })
    mocks.customerService.getById.mockResolvedValue(
      detail({
        profilePhoto: {
          photoId: 1204,
          imageId: 5740,
          originalImageId: 12040,
          url: 'https://storage.example/5740.webp',
          originalUrl: 'https://storage.example/1204.jpg',
          urlExpiresAt: '2099-01-01T00:00:00Z',
        },
      }),
    )

    page()

    await waitFor(() => {
      expect(screen.getByTestId('profile-avatar-button')).toBeInTheDocument()
    })

    await user.click(screen.getByTestId('profile-avatar-button'))

    await waitFor(() => {
      expect(screen.getByTestId('viewer-image')).toHaveAttribute(
        'src',
        'https://storage.example/1204.jpg',
      )
    })
  })

  /** US3-9: perfil vindo da conversa não tem recorte dedicado — a tela avisa. */
  it('avisa quando o perfil não tem enquadramento dedicado', async () => {
    mocks.customerService.getById.mockResolvedValue(
      detail({
        profilePhoto: {
          photoId: 1204,
          imageId: 12040,
          originalImageId: 12040,
          url: 'https://storage.example/1204.jpg',
          originalUrl: null,
          urlExpiresAt: '2099-01-01T00:00:00Z',
        },
      }),
    )

    page()

    await waitFor(() => {
      expect(screen.getByTestId('profile-without-frame')).toBeInTheDocument()
    })
  })

  it('a área de inclusão vive na aba Fotos (FR-020)', async () => {
    page()

    await waitFor(() => {
      expect(screen.getByTestId('photo-drop-button')).toBeInTheDocument()
    })

    expect(screen.getByTestId('photo-drop-button')).toHaveTextContent('Arraste para incluir uma foto')
  })
})
