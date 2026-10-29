import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { CustomerPhotoViewer } from '@/components/customerPhotos/CustomerPhotoViewer'
import { imageService } from '@/Services/imageService'

import type { CustomerPhotoViewerProps } from '@/components/customerPhotos/CustomerPhotoViewer'
import type { CustomerPhotoInfo } from '@/types/customerPhoto'

/**
 * Visualizador (T037/T053, contracts/ui.md §4): setas e teclado, coração
 * otimista, descrição com limite, excluir com confirmação e o editor de recorte
 * aberto pelo "Usar como foto de perfil".
 *
 * O `react-easy-crop` é substituído: o cropper real mede o container com
 * `ResizeObserver` (inexistente no jsdom) e carrega a imagem — nada disso é o
 * que este teste exerce. O editor próprio tem teste em `ProfilePhotoCropper`.
 */
vi.mock('react-easy-crop', () => ({
  default: ({ image }: { image?: string }) => <div data-testid="crop-stub">{image ?? ''}</div>,
}))

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

const PHOTOS = [photo(1204), photo(1200), photo(1150)]

const renderViewer = (overrides: Partial<CustomerPhotoViewerProps> = {}): CustomerPhotoViewerProps => {
  const props: CustomerPhotoViewerProps = {
    open: true,
    photos: PHOTOS,
    index: 0,
    onIndexChange: vi.fn(),
    onClose: vi.fn(),
    customerName: 'Maria Souza',
    onToggleFavorite: vi.fn().mockResolvedValue(null),
    onUpdateDescription: vi.fn().mockResolvedValue(true),
    onUseAsProfile: vi.fn().mockResolvedValue(null),
    onRemoveProfile: vi.fn().mockResolvedValue(false),
    onDelete: vi.fn().mockResolvedValue(true),
    onRequestMore: vi.fn(),
    ...overrides,
  }

  render(<CustomerPhotoViewer {...props} />)

  return props
}

describe('CustomerPhotoViewer', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('abre o original inteiro, na proporção real (FR-019, R3)', () => {
    renderViewer()

    const image = screen.getByTestId('viewer-image')

    expect(image).toHaveAttribute('src', 'https://storage.example/1204.jpg')
    expect(image.className).toContain('object-contain')
  })

  it('traz a data e a origem da foto', () => {
    renderViewer()

    expect(screen.getByText(/enviada pela tela/)).toBeInTheDocument()
  })

  it('as setas andam na ordem da grade (US1-4)', () => {
    const props = renderViewer()

    fireEvent.click(screen.getByTestId('viewer-next'))
    expect(props.onIndexChange).toHaveBeenCalledWith(1)

    fireEvent.click(screen.getByTestId('viewer-previous'))
    expect(props.onIndexChange).not.toHaveBeenCalledWith(-1)
  })

  it('as teclas ← → navegam sem fechar', () => {
    const props = renderViewer()

    fireEvent.keyDown(window, { key: 'ArrowRight' })
    expect(props.onIndexChange).toHaveBeenCalledWith(1)

    vi.mocked(props.onIndexChange).mockClear()

    fireEvent.keyDown(window, { key: 'ArrowLeft' })
    expect(props.onIndexChange).not.toHaveBeenCalled()
  })

  it('Esc fecha o visualizador', () => {
    const props = renderViewer()

    fireEvent.keyDown(window, { key: 'Escape' })
    expect(props.onClose).toHaveBeenCalled()
  })

  /** FR-008 / SC-008: o coração é `aria-pressed` e alterna na hora. */
  it('o coração chama a favorita otimista com a foto atual', () => {
    const props = renderViewer()

    const heart = screen.getByTestId('viewer-favorite')

    expect(heart).toHaveAttribute('aria-pressed', 'false')

    fireEvent.click(heart)

    expect(props.onToggleFavorite).toHaveBeenCalledTimes(1)
    expect(vi.mocked(props.onToggleFavorite).mock.calls[0]?.[0]?.photoId).toBe(1204)
  })

  it('coração cheio quando a foto já é favorita', () => {
    const props = renderViewer({
      photos: [photo(1204, { isFavorite: true }), ...PHOTOS.slice(1)],
    })

    expect(screen.getByTestId('viewer-favorite')).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Desmarcar como favorita' })).toBeInTheDocument()
    expect(props.onToggleFavorite).not.toHaveBeenCalled()
  })

  /** FR-019: a foto só pode ser perfil a partir do visualizador. */
  it('"Usar como foto de perfil" abre o editor de recorte', () => {
    renderViewer()

    expect(screen.queryByTestId('crop-stub')).not.toBeInTheDocument()

    fireEvent.click(screen.getByTestId('viewer-use-as-profile'))

    expect(screen.getByTestId('crop-stub')).toBeInTheDocument()
  })

  it('quando a foto já é o perfil, o rótulo vira "Ajustar enquadramento"', () => {
    renderViewer({ photos: [photo(1204, { isProfile: true }), ...PHOTOS.slice(1)] })

    expect(screen.getByTestId('viewer-use-as-profile')).toHaveTextContent('Ajustar enquadramento')
  })

  /** US4-3: editar a descrição manda o texto para o contexto. */
  it('edita e salva a descrição', () => {
    const props = renderViewer()

    fireEvent.click(screen.getByRole('button', { name: 'Adicionar descrição' }))
    fireEvent.change(screen.getByTestId('description-textarea'), {
      target: { value: 'provando o vestido azul' },
    })
    fireEvent.click(screen.getByTestId('description-save'))

    expect(props.onUpdateDescription).toHaveBeenCalledWith(1204, 'provando o vestido azul')
  })

  it('Ctrl+Enter salva sem tocar no botão', () => {
    const props = renderViewer({ photos: [photo(1204, { description: 'saia verde' }), ...PHOTOS.slice(1)] })

    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    fireEvent.keyDown(screen.getByTestId('description-textarea'), {
      key: 'Enter',
      ctrlKey: true,
    })

    expect(props.onUpdateDescription).toHaveBeenCalledWith(1204, 'saia verde')
  })

  /** US4-5: acima do limite o Salvar trava com aviso. */
  it('trava o Salvar com mais de 500 caracteres', () => {
    const props = renderViewer()

    fireEvent.click(screen.getByRole('button', { name: 'Adicionar descrição' }))
    fireEvent.change(screen.getByTestId('description-textarea'), {
      target: { value: 'x'.repeat(501) },
    })

    expect(screen.getByTestId('description-warning')).toHaveTextContent(
      'A descrição deve ter no máximo 500 caracteres.',
    )
    expect(screen.getByTestId('description-save')).toBeDisabled()

    fireEvent.click(screen.getByTestId('description-save'))
    expect(props.onUpdateDescription).not.toHaveBeenCalled()
  })

  /** FR-009: excluir sempre pede confirmação (skill `react-modal`). */
  it('excluir pede confirmação antes de chamar a API', () => {
    const props = renderViewer()

    fireEvent.click(screen.getByTestId('viewer-delete'))

    expect(screen.getByText('Excluir esta foto? Essa ação não pode ser desfeita.')).toBeInTheDocument()
    expect(props.onDelete).not.toHaveBeenCalled()

    fireEvent.click(screen.getByTestId('viewer-delete-confirm-confirm'))

    expect(props.onDelete).toHaveBeenCalledTimes(1)
    expect(vi.mocked(props.onDelete).mock.calls[0]?.[0]?.photoId).toBe(1204)
  })

  /** FR-015 / US3-6: a vendedora é avisada de que o perfil cai junto. */
  it('avisa que a cliente volta às iniciais quando a foto é o perfil', () => {
    renderViewer({ photos: [photo(1204, { isProfile: true }), ...PHOTOS.slice(1)] })

    fireEvent.click(screen.getByTestId('viewer-delete'))

    expect(
      screen.getByText('Maria Souza vai voltar a aparecer com as iniciais.'),
    ).toBeInTheDocument()
  })

  /** R10: link vencido renova pela rota `GET /images/{imageId}` da 004. */
  it('renova o link uma vez quando a imagem falha', async () => {
    const spy = vi.spyOn(imageService, 'getReadUrl').mockResolvedValue('https://storage.example/nova')

    renderViewer()

    fireEvent.error(screen.getByTestId('viewer-image'))

    await waitFor(() => {
      expect(spy).toHaveBeenCalledWith(12040)
    })

    await waitFor(() => {
      expect(screen.getByTestId('viewer-image')).toHaveAttribute('src', 'https://storage.example/nova')
    })

    fireEvent.error(screen.getByTestId('viewer-image'))
    expect(spy).toHaveBeenCalledTimes(1)
  })
})
