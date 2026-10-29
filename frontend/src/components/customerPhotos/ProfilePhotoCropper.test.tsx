import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ProfilePhotoCropper } from '@/components/customerPhotos/ProfilePhotoCropper'
import { cropToFile, CropToFileError } from '@/lib/cropToFile'
import { imageService } from '@/Services/imageService'

import type { CustomerPhotoInfo, CustomerProfilePhotoInfo } from '@/types/customerPhoto'

/**
 * Editor de recorte do perfil (contracts/ui.md §5, T044, US3-1/US3-2/US3-8).
 *
 * O `react-easy-crop` real mede o container com `ResizeObserver` (não existe no
 * jsdom) e carrega a imagem de verdade — nada disso é o que este teste exerce. O
 * mock entrega o `<img>` com dimensões conhecidas, avisa `onMediaLoaded`, entrega
 * a área recortada em `onCropComplete` e devolve o `maxZoom` que a tela calculou.
 *
 * O que importa aqui é o caminho do salvar em DOIS passos (canvas → `POST
 * /images` → `PUT .../profile-photo`) e a regra de que falha em qualquer passo
 * deixa o editor aberto com o motivo (research R4).
 */
const cropMock = vi.hoisted(() => ({ natural: 600 }))

vi.mock('react-easy-crop', async () => {
  // Importado dentro da fábrica: `vi.mock` é içado acima dos `import` do arquivo,
  // então referenciar o módulo no escopo do teste daria acesso antes de iniciar.
  const { useEffect } = await import('react')

  /**
   * Componente com nome de componente: o `react-easy-crop` real é uma classe que
   * mede o container com `ResizeObserver` (não existe no jsdom) e baixa a imagem
   * de verdade. Este stub entrega as mesmas três coisas que a tela usa: o `<img>`
   * com dimensões conhecidas, o tamanho da mídia e a área recortada.
   */
  const CropStub = ({
    image,
    maxZoom,
    onCropComplete,
    onMediaLoaded,
    setImageRef,
  }: {
    image?: string
    maxZoom?: number
    onCropComplete?: (area: unknown, pixels: { x: number; y: number; width: number; height: number }) => void
    onMediaLoaded?: (size: { width: number; height: number; naturalWidth: number; naturalHeight: number }) => void
    setImageRef?: (ref: { current: HTMLImageElement }) => void
  }) => {
    /* eslint-disable react-hooks/exhaustive-deps */
    useEffect(() => {
      const side = cropMock.natural

      setImageRef?.({ current: { naturalWidth: side, naturalHeight: side } as HTMLImageElement })
      onMediaLoaded?.({ width: 300, height: 300, naturalWidth: side, naturalHeight: side })
      onCropComplete?.(
        { x: 0, y: 0, width: 100, height: 100 },
        { x: 10, y: 20, width: 200, height: 200 },
      )
    }, [])
    /* eslint-enable react-hooks/exhaustive-deps */

    return (
      <div data-testid="crop-stub" data-max-zoom={String(maxZoom)}>
        {image ?? ''}
      </div>
    )
  }

  return { default: CropStub }
})

vi.mock('@/lib/cropToFile', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/cropToFile')>()

  return { ...actual, cropToFile: vi.fn() }
})

vi.mock('@/Services/imageService', () => ({
  imageService: { upload: vi.fn(), getReadUrl: vi.fn(), getById: vi.fn() },
}))

const photo: CustomerPhotoInfo = {
  photoId: 1204,
  customerId: 88,
  imageId: 12040,
  description: null,
  isFavorite: false,
  isProfile: false,
  source: 'web',
  createdAt: '2026-10-29T10:00:00Z',
  updatedAt: '2026-10-29T10:00:00Z',
  url: 'https://storage.example/1204.jpg',
  urlExpiresAt: '2099-01-01T00:00:00Z',
}

const profile: CustomerProfilePhotoInfo = {
  photoId: 1204,
  imageId: 5740,
  originalImageId: 12040,
  url: 'https://storage.example/5740.webp',
  originalUrl: 'https://storage.example/1204.jpg',
  urlExpiresAt: '2099-01-01T00:00:00Z',
}

interface CropperHarness {
  open: boolean
  onOpenChange: ReturnType<typeof vi.fn>
  photo: CustomerPhotoInfo
  isCurrentProfile: boolean
  onSave: ReturnType<typeof vi.fn>
  onRemoveProfile: ReturnType<typeof vi.fn>
}

const renderCropper = (overrides: Partial<CropperHarness> = {}): CropperHarness => {
  const props: CropperHarness = {
    open: true,
    onOpenChange: vi.fn(),
    photo,
    isCurrentProfile: false,
    onSave: vi.fn().mockResolvedValue(profile),
    onRemoveProfile: vi.fn().mockResolvedValue(true),
    ...overrides,
  }

  render(<ProfilePhotoCropper {...props} />)

  return props
}

describe('ProfilePhotoCropper', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    cropMock.natural = 600

    vi.mocked(cropToFile).mockResolvedValue(
      new File(['recorte'], 'foto-perfil.webp', { type: 'image/webp' }),
    )
    vi.mocked(imageService.upload).mockResolvedValue({
      imageId: 5740,
      mimeType: 'image/webp',
      status: 'available',
      url: 'https://storage.example/5740.webp',
      urlExpiresAt: '2099-01-01T00:00:00Z',
    })
  })

  it('abre o cropper sobre o original da foto do álbum (R4, sem enquadramento guardado)', () => {
    const props = renderCropper()

    expect(props.photo.url).toBe('https://storage.example/1204.jpg')
    expect(screen.getByTestId('modal-panel')).toBeInTheDocument()
    expect(screen.getByTestId('crop-stub')).toHaveTextContent('https://storage.example/1204.jpg')
  })

  /** US3-10: o zoom nunca deixa o quadrado abaixo de 64 px do arquivo. */
  it('aceita 4× quando o arquivo é grande o bastante', () => {
    cropMock.natural = 600
    renderCropper()

    expect(screen.getByTestId('crop-stub')).toHaveAttribute('data-max-zoom', '4')
    expect(screen.getByTestId('crop-zoom')).toHaveAttribute('max', '4')
  })

  /** 200 px de lado curto / 64 = 3 → o slider não passa de 3× nesta foto. */
  it('corta o zoom na foto pequena', () => {
    cropMock.natural = 200
    renderCropper()

    expect(screen.getByTestId('crop-zoom')).toHaveAttribute('max', '3')
  })

  /** Um arquivo menor que o lado mínimo não amplia nada (mínimo 1×). */
  it('nunca desce de 1× no zoom máximo', () => {
    cropMock.natural = 100
    renderCropper()

    expect(screen.getByTestId('crop-zoom')).toHaveAttribute('max', '1')
  })

  it('"Remover foto de perfil" não aparece quando a foto ainda não é o perfil', () => {
    renderCropper()

    expect(screen.queryByTestId('crop-remove-profile')).not.toBeInTheDocument()
  })

  it('"Remover foto de perfil" aparece quando a foto é o perfil atual', () => {
    renderCropper({ isCurrentProfile: true, photo: { ...photo, isProfile: true } })

    expect(screen.getByTestId('crop-remove-profile')).toBeInTheDocument()
  })

  /** Os dois passos do navegador antes de qualquer `PUT` (contracts/ui.md §5). */
  it('salvar gera o arquivo, sobe por POST /images e depois chama o PUT', async () => {
    const user = userEvent.setup()
    const props = renderCropper()

    await user.click(screen.getByTestId('crop-save'))

    await waitFor(() => {
      expect(props.onSave).toHaveBeenCalledWith(5740)
    })

    expect(cropToFile).toHaveBeenCalledTimes(1)
    expect(imageService.upload).toHaveBeenCalledTimes(1)
    expect(props.onOpenChange).toHaveBeenCalledWith(false)
  })

  /** Recusa do servidor: o editor continua aberto, com o botão de novo. */
  it('falha no PUT deixa o editor aberto', async () => {
    const user = userEvent.setup()
    const props = renderCropper({ onSave: vi.fn().mockResolvedValue(null) })

    await user.click(screen.getByTestId('crop-save'))

    await waitFor(() => {
      expect(props.onSave).toHaveBeenCalled()
    })

    expect(props.onOpenChange).not.toHaveBeenCalledWith(false)
    expect(screen.getByTestId('crop-save')).toBeEnabled()
  })

  /** Canvas indisponível: o motivo aparece em vez de um botão mudo. */
  it('mostra o motivo quando o navegador não consegue recortar', async () => {
    const user = userEvent.setup()
    const props = renderCropper()

    vi.mocked(cropToFile).mockRejectedValue(new CropToFileError('no-canvas'))

    await user.click(screen.getByTestId('crop-save'))

    await waitFor(() => {
      expect(screen.getByTestId('crop-message')).toHaveTextContent(
        'Este navegador não consegue desenhar a foto.',
      )
    })

    expect(props.onSave).not.toHaveBeenCalled()
    expect(props.onOpenChange).not.toHaveBeenCalledWith(false)
  })

  it('o canvas contaminado tem aviso próprio (CORS do storage)', async () => {
    const user = userEvent.setup()
    renderCropper()

    vi.mocked(cropToFile).mockRejectedValue(new CropToFileError('security'))

    await user.click(screen.getByTestId('crop-save'))

    await waitFor(() => {
      expect(screen.getByTestId('crop-message')).toHaveTextContent(/CORS/)
    })
  })

  it('remover o perfil fecha o editor quando deu certo', async () => {
    const user = userEvent.setup()
    const props = renderCropper({ isCurrentProfile: true })

    await user.click(screen.getByTestId('crop-remove-profile'))

    await waitFor(() => {
      expect(props.onRemoveProfile).toHaveBeenCalled()
    })

    expect(props.onOpenChange).toHaveBeenCalledWith(false)
  })

  /** US3-1: o quadrado se move e se redimensiona por pinça, roda e slider. */
  it('o slider de zoom vai de 1× ao máximo calculado, com rótulo', () => {
    renderCropper()

    const slider = screen.getByLabelText('Zoom')

    expect(slider).toHaveAttribute('min', '1')
    expect(slider).toHaveAttribute('max', '4')
    expect(slider).toHaveAttribute('type', 'range')
  })
})
