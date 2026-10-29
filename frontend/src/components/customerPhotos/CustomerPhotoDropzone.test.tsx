import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { toast } from 'sonner'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CustomerPhotoDropzone } from '@/components/customerPhotos/CustomerPhotoDropzone'

import { MAX_PHOTO_BYTES } from '@/types/customerPhoto'

/** Os avisos são do `sonner` (skill `react-alert`): o teste confere a frase exata. */
vi.mock('sonner', () => ({
  toast: { warning: vi.fn(), error: vi.fn(), success: vi.fn(), info: vi.fn() },
}))

/**
 * Área de inclusão (T037, contracts/ui.md §3, FR-020/FR-021): o arraste com os
 * eventos NATIVOS da janela, o `input` aberto pelo toque, a recusa local de tipo
 * e de tamanho com o nome do arquivo no aviso, e o ignore de tudo que não é
 * arquivo (texto e link solto).
 */
const photoFile = (name = 'vestido.jpg', type = 'image/jpeg'): File =>
  new File(['bytes'], name, { type })

/** Arraste de verdade: o jsdom não tem `DataTransfer`, então o objeto é montado. */
const fireDrag = (
  type: 'dragenter' | 'dragover' | 'dragleave' | 'drop',
  init: { files?: unknown[]; types?: string[] },
): void => {
  const event = new Event(type, { bubbles: true, cancelable: true })

  Object.defineProperty(event, 'dataTransfer', {
    value: {
      files: init.files ?? [],
      types: init.types ?? (init.files === undefined ? ['text/plain'] : ['Files']),
      dropEffect: '',
    },
  })

  window.dispatchEvent(event)
}

describe('CustomerPhotoDropzone', () => {
  const onFiles = vi.fn()

  beforeEach(() => {
    onFiles.mockClear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('mostra o texto do contrato, "Arraste para incluir uma foto" (FR-020)', () => {
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    expect(screen.getByTestId('photo-drop-button')).toHaveTextContent(
      'Arraste para incluir uma foto',
    )
  })

  it('no toque o clique abre o seletor de arquivos escondido (US2-4)', () => {
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    const click = vi.spyOn(HTMLInputElement.prototype, 'click')

    fireEvent.click(screen.getByTestId('photo-drop-button'))

    expect(click).toHaveBeenCalledTimes(1)
    expect(screen.getByTestId('photo-input')).toHaveAttribute(
      'accept',
      'image/jpeg,image/png,image/webp',
    )
    expect(screen.getByTestId('photo-input')).toHaveAttribute('multiple')
  })

  it('aceita os arquivos escolhidos no input e limpa a seleção', async () => {
    const user = userEvent.setup()
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    await user.upload(screen.getByTestId('photo-input'), [photoFile('a.jpg'), photoFile('b.png', 'image/png')])

    expect(onFiles).toHaveBeenCalledTimes(1)
    expect(onFiles.mock.calls[0]?.[0]).toHaveLength(2)
  })

  it('drop de vários arquivos manda todos de uma vez (US2-2)', () => {
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    fireDrag('drop', { files: [photoFile('um.jpg'), photoFile('dois.webp', 'image/webp')] })

    expect(onFiles).toHaveBeenCalledTimes(1)
    expect((onFiles.mock.calls[0]?.[0] as File[]).map((file) => file.name)).toEqual([
      'um.jpg',
      'dois.webp',
    ])
  })

  /** Edge case "arraste de algo que não é foto": recusa local, com o nome no aviso. */
  it('recusa tipo fora da lista antes de qualquer requisição', () => {
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    fireDrag('drop', { files: [photoFile('IMG.heic', 'image/heic')] })

    expect(onFiles).not.toHaveBeenCalled()
    expect(toast.warning).toHaveBeenCalledWith('IMG.heic: formato não aceito')
  })

  it('recusa tamanho acima de 10 MB', () => {
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    const huge = {
      name: 'gigante.jpg',
      type: 'image/jpeg',
      size: MAX_PHOTO_BYTES + 1,
    } as unknown as File

    fireDrag('drop', { files: [huge] })

    expect(onFiles).not.toHaveBeenCalled()
    expect(toast.warning).toHaveBeenCalledWith('gigante.jpg: acima do limite de 10 MB')
  })

  /** US2-3: um arquivo recusado não segura os outros do lote. */
  it('aceita os bons e avisa só sobre o recusado', () => {
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    fireDrag('drop', {
      files: [photoFile('bom.jpg'), photoFile('nota.pdf', 'application/pdf')],
    })

    expect(onFiles).toHaveBeenCalledTimes(1)
    expect((onFiles.mock.calls[0]?.[0] as File[]).map((f) => f.name)).toEqual(['bom.jpg'])
    expect(toast.warning).toHaveBeenCalledWith('nota.pdf: formato não aceito')
  })

  /** Edge case da spec: texto e link solto não são arquivos — a área ignora. */
  it('ignora arraste que não traz arquivo', () => {
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    fireDrag('drop', { types: ['text/plain'] })

    expect(onFiles).not.toHaveBeenCalled()
  })

  it('destaca a área enquanto o arraste está dentro da aba', async () => {
    render(<CustomerPhotoDropzone onFiles={onFiles} />)

    expect(screen.getByTestId('photo-dropzone')).toHaveAttribute('data-dragging', 'false')

    fireDrag('dragenter', { files: [photoFile()] })

    await waitFor(() => {
      expect(screen.getByTestId('photo-dropzone')).toHaveAttribute('data-dragging', 'true')
    })

    fireDrag('dragleave', { files: [photoFile()] })

    await waitFor(() => {
      expect(screen.getByTestId('photo-dropzone')).toHaveAttribute('data-dragging', 'false')
    })
  })
})
