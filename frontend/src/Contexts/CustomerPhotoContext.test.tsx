import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { CustomerPhotoProvider } from '@/Contexts/CustomerPhotoContext'
import { useCustomerPhotos } from '@/hooks/useCustomerPhotos'
import { UnauthorizedError } from '@/Services/apiClient'

import type { ReactNode } from 'react'
import type { CustomerPhotoInfo } from '@/types/customerPhoto'

/**
 * CustomerPhotoContext (T031): cursor acumulado, inserção no topo, dedup com
 * `created=false`, favorita otimista com estorno e exclusão. Os serviços são
 * substituídos — o que se exercita aqui é a decisão do estado, não o transporte.
 */
const mocks = vi.hoisted(() => ({
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
  imageService: { upload: vi.fn(), getReadUrl: vi.fn() },
  toast: { warning: vi.fn(), error: vi.fn(), success: vi.fn(), info: vi.fn() },
}))

vi.mock('@/Services/customerPhotoService', () => ({
  customerPhotoService: mocks.customerPhotoService,
}))

vi.mock('@/Services/imageService', () => ({ imageService: mocks.imageService }))

vi.mock('sonner', () => ({ toast: mocks.toast }))

const wrapper = ({ children }: { children: ReactNode }) => (
  <CustomerPhotoProvider>{children}</CustomerPhotoProvider>
)

const shot = (photoId: number, overrides: Partial<CustomerPhotoInfo> = {}): CustomerPhotoInfo => ({
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

const file = (name = 'foto.jpg'): File => new File([new Uint8Array([1])], name, { type: 'image/jpeg' })

describe('CustomerPhotoContext', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    // jsdom não implementa os dois métodos de `URL`; o ambiente é por arquivo,
    // então o stub fica definido até o fim da suíte — inclusive para o `cleanup`
    // do RTL, que desmonta o provider DEPOIS do `afterEach` deste arquivo (e a
    // limpeza do provider revoga as miniaturas locais em voo).
    Object.defineProperty(URL, 'createObjectURL', {
      value: vi.fn(() => 'blob:preview-local'),
      configurable: true,
      writable: true,
    })
    Object.defineProperty(URL, 'revokeObjectURL', {
      value: vi.fn(),
      configurable: true,
      writable: true,
    })

    mocks.customerPhotoService.list.mockResolvedValue({ items: [], nextCursor: null })
  })

  it('acumula a próxima página pelo cursor devolvido (R8)', async () => {
    mocks.customerPhotoService.list
      .mockResolvedValueOnce({ items: [shot(1204), shot(1200)], nextCursor: 1180 })
      .mockResolvedValueOnce({ items: [shot(1150)], nextCursor: null })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    expect(result.current.photos.map((photo) => photo.photoId)).toEqual([1204, 1200])
    expect(result.current.hasMore).toBe(true)

    await act(async () => {
      await result.current.loadMore()
    })

    expect(mocks.customerPhotoService.list).toHaveBeenLastCalledWith({
      customerId: 88,
      cursor: 1180,
      favorites: false,
    })
    expect(result.current.photos.map((photo) => photo.photoId)).toEqual([1204, 1200, 1150])
    expect(result.current.hasMore).toBe(false)
  })

  /** FR-010 / US4-6: o filtro troca a lista e a posição de cada um fica na página. */
  it('filtra favoritas e volta a "Todas" sem pedir de novo', async () => {
    mocks.customerPhotoService.list
      .mockResolvedValueOnce({ items: [shot(2, { isFavorite: true }), shot(1)], nextCursor: null })
      .mockResolvedValueOnce({ items: [shot(2, { isFavorite: true })], nextCursor: null })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    await act(async () => {
      await result.current.setFavorites(true)
    })

    expect(mocks.customerPhotoService.list).toHaveBeenLastCalledWith({
      customerId: 88,
      cursor: null,
      favorites: true,
    })
    expect(result.current.photos.map((photo) => photo.photoId)).toEqual([2])

    await act(async () => {
      await result.current.setFavorites(false)
    })

    // "Todas" já estava na memória: voltou para onde estava, sem nova chamada.
    expect(mocks.customerPhotoService.list).toHaveBeenCalledTimes(2)
    expect(result.current.photos.map((photo) => photo.photoId)).toEqual([2, 1])
  })

  /** R7: os dois passos, um arquivo por par, e a foto entra no topo (US2-1). */
  it('envia em dois passos e põe a foto no topo da grade', async () => {
    const created = shot(1300)

    mocks.customerPhotoService.list.mockResolvedValue({ items: [shot(1204)], nextCursor: null })
    mocks.imageService.upload.mockResolvedValue({ imageId: 5740 })
    mocks.customerPhotoService.attach.mockResolvedValue({
      photo: created,
      created: true,
      alsoInOtherCustomers: false,
    })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    act(() => {
      result.current.enqueueFiles([file()])
    })

    expect(result.current.uploads).toHaveLength(1)
    expect(result.current.uploads[0]?.state).toBe('uploading')

    await waitFor(() => {
      expect(result.current.photos.map((photo) => photo.photoId)).toEqual([1300, 1204])
    })

    expect(mocks.imageService.upload).toHaveBeenCalledWith(expect.objectContaining({ name: 'foto.jpg' }))
    expect(mocks.customerPhotoService.attach).toHaveBeenCalledWith(88, 5740)
    expect(result.current.uploads).toHaveLength(0)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-local')
  })

  /** US2-5 / FR-006: `created=false` não duplica célula; a vendedora é avisada. */
  it('descarta a célula e avisa quando a foto já estava no álbum', async () => {
    mocks.customerPhotoService.list.mockResolvedValue({ items: [shot(1204)], nextCursor: null })
    mocks.imageService.upload.mockResolvedValue({ imageId: 5740 })
    mocks.customerPhotoService.attach.mockResolvedValue({
      photo: shot(1204),
      created: false,
      alsoInOtherCustomers: false,
    })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    act(() => {
      result.current.enqueueFiles([file()])
    })

    await waitFor(() => {
      expect(result.current.uploads).toHaveLength(0)
    })

    expect(result.current.photos.map((photo) => photo.photoId)).toEqual([1204])
    expect(mocks.toast.warning).toHaveBeenCalledWith('Essa foto já estava no álbum.')
  })

  /** US2-3: a foto que falhou fica na tela com o motivo e as duas ações. */
  it('mantém a célula com o motivo quando o envio falha', async () => {
    mocks.customerPhotoService.list.mockResolvedValue({ items: [], nextCursor: null })
    mocks.imageService.upload.mockRejectedValue(new Error('Tipo de imagem não aceito.'))

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    act(() => {
      result.current.enqueueFiles([file()])
    })

    await waitFor(() => {
      expect(result.current.uploads[0]?.state).toBe('error')
    })

    expect(result.current.uploads[0]?.error).toBe('Tipo de imagem não aceito.')

    // "Descartar" tira a célula e revoga o blob local.
    act(() => {
      result.current.dismissUpload(result.current.uploads[0]?.localId ?? '')
    })

    expect(result.current.uploads).toHaveLength(0)
    expect(URL.revokeObjectURL).toHaveBeenCalled()
  })

  /** research R7: três envios em paralelo, nunca mais que isso de uma vez. */
  it('manda no máximo três arquivos ao mesmo tempo', async () => {
    mocks.customerPhotoService.list.mockResolvedValue({ items: [], nextCursor: null })

    interface Gate {
      release: (image: { imageId: number }) => void
    }

    const gates: Gate[] = []

    mocks.imageService.upload.mockImplementation(() => {
      // O `executor` do `Promise` roda na hora: quando o `push` acontece,
      // `release` já é o resolutor verdadeiro daquela chamada.
      let release: Gate['release'] = () => undefined

      const promise = new Promise<{ imageId: number }>((resolve) => {
        release = resolve
      })

      gates.push({ release })

      return promise
    })

    mocks.customerPhotoService.attach.mockResolvedValue({
      photo: shot(1300),
      created: true,
      alsoInOtherCustomers: false,
    })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    act(() => {
      result.current.enqueueFiles([
        file('1.jpg'),
        file('2.jpg'),
        file('3.jpg'),
        file('4.jpg'),
        file('5.jpg'),
      ])
    })

    // Cinco arquivos na fila, três em voo, dois esperando a vez.
    expect(gates).toHaveLength(3)
    expect(result.current.uploads).toHaveLength(5)

    await act(async () => {
      gates[0]?.release({ imageId: 7001 })

      // Deixa o segundo passo (`attach`) e a liberação da vaga rodarem.
      await Promise.resolve()
    })

    // A vaga liberada entra imediatamente (US2-2: cada uma com seu indicador).
    await waitFor(() => {
      expect(gates).toHaveLength(4)
    })

    expect(result.current.uploads).toHaveLength(4)
  })

  it('marca favorita pelo servidor e desmarca por outro verbo (R11)', async () => {
    const photo = shot(1204)

    mocks.customerPhotoService.list.mockResolvedValue({ items: [photo], nextCursor: null })
    mocks.customerPhotoService.favorite.mockResolvedValue({ ...photo, isFavorite: true })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    await act(async () => {
      await result.current.toggleFavorite(result.current.photos[0] ?? photo)
    })

    expect(mocks.customerPhotoService.favorite).toHaveBeenCalledWith(88, 1204)
    expect(result.current.photos[0]?.isFavorite).toBe(true)

    mocks.customerPhotoService.unfavorite.mockResolvedValue({ ...photo, isFavorite: false })

    await act(async () => {
      await result.current.toggleFavorite(result.current.photos[0] ?? photo)
    })

    expect(mocks.customerPhotoService.unfavorite).toHaveBeenCalledWith(88, 1204)
    expect(result.current.photos[0]?.isFavorite).toBe(false)
  })

  /** SC-008: o coração é otimista e estorna com aviso se a API recusar. */
  it('estorna a favorita otimista quando a API recusa', async () => {
    const photo = shot(1204, { isFavorite: false })

    mocks.customerPhotoService.list.mockResolvedValue({ items: [photo], nextCursor: null })
    mocks.customerPhotoService.favorite.mockRejectedValue(new Error('Foto não encontrada.'))

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    await act(async () => {
      await expect(result.current.toggleFavorite(photo)).resolves.toBeNull()
    })

    expect(result.current.photos[0]?.isFavorite).toBe(false)
    expect(mocks.toast.error).toHaveBeenCalledWith('Não foi possível salvar a favorita.')
  })

  /** 401 é do `AuthContext`: o álbum não empilha aviso nem estorna com ruído. */
  it('ignora UnauthorizedError sem aviso', async () => {
    const photo = shot(1204)

    mocks.customerPhotoService.list.mockResolvedValue({ items: [photo], nextCursor: null })
    mocks.customerPhotoService.favorite.mockRejectedValue(new UnauthorizedError())

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    await act(async () => {
      await result.current.toggleFavorite(photo)
    })

    expect(mocks.toast.error).not.toHaveBeenCalled()
  })

  it('descreve pelo servidor e aplica o resultado na lista', async () => {
    const photo = shot(1204)

    mocks.customerPhotoService.list.mockResolvedValue({ items: [photo], nextCursor: null })
    mocks.customerPhotoService.setDescription.mockResolvedValue({
      ...photo,
      description: 'provando o vestido azul',
    })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    let saved: CustomerPhotoInfo | null = null

    await act(async () => {
      saved = await result.current.updateDescription(1204, 'provando o vestido azul')
    })

    expect(saved).not.toBeNull()
    expect(result.current.photos[0]?.description).toBe('provando o vestido azul')
  })

  it('remove a foto da lista depois do 204', async () => {
    mocks.customerPhotoService.list.mockResolvedValue({
      items: [shot(1204), shot(1200)],
      nextCursor: null,
    })
    mocks.customerPhotoService.remove.mockResolvedValue(undefined)

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    await act(async () => {
      await result.current.removePhoto(1204)
    })

    expect(mocks.customerPhotoService.remove).toHaveBeenCalledWith(88, 1204)
    expect(result.current.photos.map((photo) => photo.photoId)).toEqual([1200])
  })

  /** FR-011: uma cliente, um perfil — a marca sai da foto anterior. */
  it('marca a nova foto de perfil e tira a marca da antiga', async () => {
    mocks.customerPhotoService.list.mockResolvedValue({
      items: [shot(1204, { isProfile: true }), shot(1200)],
      nextCursor: null,
    })
    mocks.customerPhotoService.setProfile.mockResolvedValue({
      photoId: 1200,
      imageId: 5740,
      originalImageId: 12000,
      url: 'https://storage.example/5740.webp',
      originalUrl: 'https://storage.example/12000.jpg',
      urlExpiresAt: '2099-01-01T00:00:00Z',
    })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    await act(async () => {
      await result.current.setProfilePhoto(1200, 5740)
    })

    expect(result.current.photos.find((photo) => photo.photoId === 1200)?.isProfile).toBe(true)
    expect(result.current.photos.find((photo) => photo.photoId === 1204)?.isProfile).toBe(false)
  })

  it('limpa o álbum e a fila ao sair da tela', async () => {
    mocks.customerPhotoService.list.mockResolvedValue({ items: [shot(1204)], nextCursor: null })

    const { result } = renderHook(() => useCustomerPhotos(), { wrapper })

    await act(async () => {
      await result.current.loadPhotos(88)
    })

    act(() => {
      result.current.clearPhotos()
    })

    expect(result.current.photos).toHaveLength(0)
    expect(result.current.customerId).toBeNull()
    expect(result.current.notFound).toBe(false)
  })
})
