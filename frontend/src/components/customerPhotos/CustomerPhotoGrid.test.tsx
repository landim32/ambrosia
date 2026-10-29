import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { CustomerPhotoGrid } from '@/components/customerPhotos/CustomerPhotoGrid'

import type { CustomerPhotoGridProps } from '@/components/customerPhotos/CustomerPhotoGrid'
import type { CustomerPhotoInfo, PhotoUploadItem } from '@/types/customerPhoto'

/**
 * Grade de fotos (T037, contracts/ui.md §2): ordem, coração, vazia, esqueleto,
 * `loading="lazy"`, alt e abrir o visualizador. A grade é presentacional — tudo
 * que ela faz chega por prop, então o teste não precisa de provider nenhum.
 */
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

const upload = (overrides: Partial<PhotoUploadItem> = {}): PhotoUploadItem => ({
  localId: 'u1',
  fileName: 'IMG_0001.jpg',
  previewUrl: 'blob:preview',
  file: new File(['x'], 'IMG_0001.jpg', { type: 'image/jpeg' }),
  state: 'uploading',
  error: null,
  ...overrides,
})

const renderGrid = (overrides: Partial<CustomerPhotoGridProps> = {}): CustomerPhotoGridProps => {
  const props: CustomerPhotoGridProps = {
    customerName: 'Maria Souza',
    photos: [photo(1204), photo(1200)],
    uploads: [],
    favorites: false,
    loading: false,
    loadingMore: false,
    hasMore: false,
    error: null,
    onFavoritesChange: vi.fn(),
    onOpen: vi.fn(),
    onLoadMore: vi.fn(),
    onFiles: vi.fn(),
    onRetryUpload: vi.fn(),
    onDismissUpload: vi.fn(),
    onRetry: vi.fn(),
    ...overrides,
  }

  render(<CustomerPhotoGrid {...props} />)

  return props
}

describe('CustomerPhotoGrid', () => {
  it('mostra uma célula por foto, na ordem recebida (mais recente primeiro)', () => {
    renderGrid()

    const images = screen.getAllByRole('img')

    expect(images.map((image) => image.getAttribute('src'))).toEqual([
      'https://storage.example/1204.jpg',
      'https://storage.example/1200.jpg',
    ])
  })

  /** R3: a grade mostra o ORIGINAL — não existe `thumbnailUrl` em lugar nenhum. */
  it('usa o original com lazy e async, cortado só pelo CSS', () => {
    renderGrid({ photos: [photo(1204)] })

    const image = screen.getByRole('img')

    expect(image).toHaveAttribute('src', 'https://storage.example/1204.jpg')
    expect(image).toHaveAttribute('loading', 'lazy')
    expect(image).toHaveAttribute('decoding', 'async')
    expect(image.className).toContain('object-cover')
  })

  it('o alt é a descrição quando existe, e o nome com a data quando não', () => {
    renderGrid({ photos: [photo(1204, { description: 'provando o vestido azul' })] })

    expect(screen.getByRole('img')).toHaveAccessibleName('provando o vestido azul')
  })

  it('sem descrição o alt traz o nome da cliente e a data', () => {
    renderGrid()

    const alt = screen.getAllByRole('img')[0]?.getAttribute('alt') ?? ''

    expect(alt).toContain('Maria Souza')
    expect(alt.startsWith('Foto de Maria Souza de')).toBe(true)
  })

  /** FR-022: coração visível nas favoritas da grade. */
  it('marca favoritas com coração, e só nelas', () => {
    renderGrid({ photos: [photo(1204, { isFavorite: true }), photo(1200)] })

    expect(screen.getAllByTestId('photo-favorite-mark')).toHaveLength(1)
  })

  it('desenha nove esqueletos no primeiro carregamento', () => {
    renderGrid({ loading: true, photos: [] })

    expect(screen.getByTestId('photo-skeleton')).toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Carregando fotos' })).toBeInTheDocument()
    expect(document.querySelectorAll('[data-testid="photo-skeleton"] > div')).toHaveLength(9)
  })

  /** US1-6: álbum vazio com a frase com o nome e a área de inclusão. */
  it('mostra a frase de álbum vazio com o nome da cliente', () => {
    renderGrid({ photos: [] })

    expect(screen.getByTestId('empty-state')).toHaveTextContent('Ainda não há fotos de Maria Souza.')
    expect(screen.getByTestId('photo-drop-button')).toBeInTheDocument()
  })

  it('cada célula é um botão que abre o visualizador naquela foto', () => {
    const props = renderGrid()

    const [, second] = screen.getAllByTestId('photo-cell')

    if (!second) throw new Error('a grade não desenhou as duas células')

    fireEvent.click(second)

    expect(props.onOpen).toHaveBeenCalledTimes(1)
    expect(vi.mocked(props.onOpen).mock.calls[0]?.[0]?.photoId).toBe(1200)
  })

  it('troca o filtro pelos botões Todas e Favoritas (FR-010)', () => {
    const props = renderGrid()

    fireEvent.click(screen.getByRole('button', { name: 'Favoritas' }))
    expect(props.onFavoritesChange).toHaveBeenCalledWith(true)

    fireEvent.click(screen.getByRole('button', { name: 'Todas' }))
    expect(props.onFavoritesChange).toHaveBeenCalledWith(false)
  })

  /** FR-021: estado por item, com miniatura local enquanto dura o envio. */
  it('diz "enviando" na célula provisória do primeiro passo', () => {
    renderGrid({ photos: [], uploads: [upload({ state: 'uploading' })] })

    expect(screen.getByTestId('photo-upload-uploading')).toBeInTheDocument()
    expect(screen.getByText('enviando')).toBeInTheDocument()
  })

  it('diz "registrando" entre os dois passos', () => {
    renderGrid({ photos: [], uploads: [upload({ localId: 'u2', state: 'attaching' })] })

    expect(screen.getByText('registrando')).toBeInTheDocument()
  })

  /** US2-3: a célula com erro traz o motivo e as duas ações. */
  it('dá "Tentar de novo" e "Descartar" na célula com erro', () => {
    const props = renderGrid({
      photos: [],
      uploads: [upload({ state: 'error', error: 'Tipo de imagem não aceito.' })],
    })

    expect(screen.getByText('Tipo de imagem não aceito.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }))
    expect(props.onRetryUpload).toHaveBeenCalledWith('u1')

    fireEvent.click(screen.getByRole('button', { name: 'Descartar' }))
    expect(props.onDismissUpload).toHaveBeenCalledWith('u1')
  })

  /** A área de inclusão fica dentro da grade e repassa os arquivos. */
  it('tem a área de inclusão com o texto do contrato', () => {
    renderGrid()

    expect(screen.getByTestId('photo-drop-button')).toHaveTextContent('Arraste para incluir uma foto')
  })

  it('mostra o erro de consulta com "Tentar de novo"', () => {
    const props = renderGrid({ photos: [], error: 'Não foi possível conectar' })

    expect(screen.getByTestId('error-state')).toHaveTextContent('Não foi possível conectar')

    fireEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }))
    expect(props.onRetry).toHaveBeenCalled()
  })
})
