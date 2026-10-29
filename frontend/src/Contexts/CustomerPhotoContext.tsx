import {
  createContext,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'

import { notifyError, toUserMessage } from '@/lib/errors'
import { NotFoundError, UnauthorizedError } from '@/Services/apiClient'
import { customerPhotoService } from '@/Services/customerPhotoService'
import { imageService } from '@/Services/imageService'

import type { CustomerPhotoPage } from '@/types/customerPhoto'
import type {
  CustomerPhotoInfo,
  CustomerProfilePhotoInfo,
  PhotoUploadItem,
  PhotoUploadState,
} from '@/types/customerPhoto'
import { PHOTO_URL_REFRESH_MARGIN_MS, PHOTO_UPLOAD_CONCURRENCY } from '@/types/customerPhoto'

/**
 * Estado do álbum de fotos da cliente (feature 009, contracts/ui.md §2 a §4,
 * skill `react-architecture`).
 *
 * Quatro coisas moram aqui, e só elas:
 *
 * 1. **Páginas acumuladas por cursor** (research R8), guardadas **por filtro** — é
 *    o que devolve a vendedora para o mesmo lugar ao trocar "Todas" ↔ "Favoritas"
 *    (FR-010). Uma foto nova entra no TOPO da primeira página, e é exatamente por
 *    isso que a grade não usa `page`/`pageSize`.
 * 2. **Fila de envio** com até `PHOTO_UPLOAD_CONCURRENCY` arquivos em voo, cada um
 *    nos dois passos `POST /images` → `POST .../photos` (research R7), com estado
 *    visível por item (FR-021).
 * 3. **Favorita otimista** com estorno: o coração vira antes da resposta e volta
 *    com aviso se a API recusar (SC-008, FR-008).
 * 4. **Links vencidos**: cada página sabe o `urlExpiresAt` dos seus itens, e
 *    `ensureFresh` relê pelo MESMO cursor a página que está para vencer (R10).
 *    O link individual quebrado é renovado no componente, por
 *    `imageService.getReadUrl(imageId)` — a rota `GET /images/{imageId}` da 004.
 *
 * Erros: `UnauthorizedError` é IGNORADO (quem desloga e avisa é o `AuthContext`,
 * mesmo critério de `CustomerContext`); 404 vira `notFound` e tem tela própria; os
 * demais viram `error` + toast com "Tentar de novo" (FR-025/FR-026).
 *
 * Páginas vivem em `ref` (com `viewsVersion` avisando o React) porque as leituras
 * são sobrepostas por cursor: deixar o estado nas `deps` dos `useCallback` faria a
 * identidade de todo método mudar a cada foto carregada.
 */

interface PageRecord {
  /** Cursor que produziu esta página; `null` = primeira. Relê-la volta ao mesmo ponto. */
  cursor: number | null
  items: CustomerPhotoInfo[]
}

interface ViewState {
  pages: PageRecord[]
  /** `photoId` da última foto da última página; `null` no fim da lista. */
  nextCursor: number | null
  /** `true` depois que a primeira página deste filtro chegou. */
  loaded: boolean
}

interface PhotoViews {
  all: ViewState
  favorites: ViewState
}

export interface CustomerPhotoContextType {
  /** Cliente cujo álbum está carregado; `null` antes de `loadPhotos`. */
  customerId: number | null
  photos: CustomerPhotoInfo[]
  /** Arquivos da fila (em envio, com erro, ou aguardando "Tentar de novo"). */
  uploads: PhotoUploadItem[]
  favorites: boolean
  /** Primeira página do filtro corrente em voo. */
  loading: boolean
  /** Página seguinte em voo (rolagem infinita, US1-7). */
  loadingMore: boolean
  /** Ainda há próxima página a pedir. */
  hasMore: boolean
  error: string | null
  /** 404: cliente inexistente ou de outra vendedora (FR-025). */
  notFound: boolean
  loadPhotos: (customerId: number, options?: { favorites?: boolean }) => Promise<void>
  /** Troca o filtro, retomando a posição já alcançada dele (FR-010, US4-6). */
  setFavorites: (favorites: boolean) => Promise<void>
  /** Próxima página pelo cursor corrente (sentinela da grade). */
  loadMore: () => Promise<void>
  /** Relê pelo mesmo cursor as páginas com link vencendo (R10). */
  ensureFresh: () => Promise<void>
  enqueueFiles: (files: File[]) => void
  retryUpload: (localId: string) => void
  dismissUpload: (localId: string) => void
  /** Coração otimista; devolve a foto salva, ou `null` (estado já estornado). */
  toggleFavorite: (photo: CustomerPhotoInfo) => Promise<CustomerPhotoInfo | null>
  updateDescription: (
    photoId: number,
    description: string | null,
  ) => Promise<CustomerPhotoInfo | null>
  /** `true` quando a foto saiu do álbum (204). */
  removePhoto: (photoId: number) => Promise<boolean>
  /** Recorte do navegador já enviado por `POST /images` (R4). */
  setProfilePhoto: (photoId: number, imageId?: number) => Promise<CustomerProfilePhotoInfo | null>
  clearProfilePhoto: () => Promise<boolean>
  /** Repete a última carga que falhou (FR-026). */
  retryPhotos: () => void
  /** Saiu da tela: descarta páginas e fila, revogando os `objectURL`. */
  clearPhotos: () => void
  clearError: () => void
}

export const CustomerPhotoContext = createContext<CustomerPhotoContextType | undefined>(undefined)

const emptyView = (): ViewState => ({ pages: [], nextCursor: null, loaded: false })

const emptyViews = (): PhotoViews => ({ all: emptyView(), favorites: emptyView() })

const keyOf = (favorites: boolean): keyof PhotoViews => (favorites ? 'favorites' : 'all')

const itemsOf = (view: ViewState): CustomerPhotoInfo[] =>
  view.pages.reduce<CustomerPhotoInfo[]>((acc, page) => acc.concat(page.items), [])

const mapView = (
  view: ViewState,
  patch: (photo: CustomerPhotoInfo) => CustomerPhotoInfo,
): ViewState => ({
  ...view,
  pages: view.pages.map((page) => ({ ...page, items: page.items.map(patch) })),
})

const withoutPhotoIn = (view: ViewState, photoId: number): ViewState => ({
  ...view,
  pages: view.pages.map((page) => ({
    ...page,
    items: page.items.filter((item) => item.photoId !== photoId),
  })),
})

const mapAll = (
  views: PhotoViews,
  patch: (photo: CustomerPhotoInfo) => CustomerPhotoInfo,
): PhotoViews => ({
  all: mapView(views.all, patch),
  favorites: mapView(views.favorites, patch),
})

const dropPhoto = (views: PhotoViews, photoId: number): PhotoViews => ({
  all: withoutPhotoIn(views.all, photoId),
  favorites: withoutPhotoIn(views.favorites, photoId),
})

/** Substitui a foto pelo que o servidor devolveu, mantendo a posição na grade. */
const applyPhoto = (views: PhotoViews, photo: CustomerPhotoInfo): PhotoViews =>
  mapAll(views, (item) => (item.photoId === photo.photoId ? photo : item))

/** Insere no topo da primeira página do filtro corrente (fotos recém-incluídas). */
const prependPhoto = (
  views: PhotoViews,
  key: keyof PhotoViews,
  photo: CustomerPhotoInfo,
): PhotoViews => {
  const view = views[key]
  const first = view.pages[0]
  const pages =
    first === undefined
      ? [{ cursor: null, items: [photo] }]
      : [{ ...first, items: [photo, ...first.items] }, ...view.pages.slice(1)]

  return { ...views, [key]: { ...view, pages } }
}

/**
 * Coração nos dois filtros: "Todas" só troca a marca; "Favoritas" perde o item
 * quando ele deixa de ser favorito — um não-favorita não tem o que fazer lá.
 */
const setFavorite = (
  views: PhotoViews,
  photoId: number,
  isFavorite: boolean,
): PhotoViews => ({
  all: mapView(views.all, (item) =>
    item.photoId === photoId ? { ...item, isFavorite } : item,
  ),
  favorites: isFavorite
    ? mapView(views.favorites, (item) =>
        item.photoId === photoId ? { ...item, isFavorite } : item,
      )
    : withoutPhotoIn(views.favorites, photoId),
})

/** Marca a foto como perfil, tirando a marca das demais (uma cliente, um perfil). */
const setProfile = (views: PhotoViews, photoId: number): PhotoViews =>
  mapAll(views, (item) => {
    if (item.photoId === photoId) return { ...item, isProfile: true }

    return item.isProfile ? { ...item, isProfile: false } : item
  })

/** `true` quando a página tem link vencendo dentro da folga (research R10). */
const pageIsStale = (page: PageRecord, now: number): boolean =>
  page.items.some((item) => {
    const expiresAt = Date.parse(item.urlExpiresAt)

    return Number.isNaN(expiresAt) || expiresAt - now < PHOTO_URL_REFRESH_MARGIN_MS
  })

type PageOutcome =
  | { status: 'ok'; page: CustomerPhotoPage }
  | { status: 'skip' }
  | { status: 'error'; error: unknown }

export const CustomerPhotoProvider = ({ children }: { children: ReactNode }) => {
  const { t } = useTranslation()

  /** O `t` muda de identidade; os callbacks assíncronos leem o corrente. */
  const translateRef = useRef(t)

  useEffect(() => {
    translateRef.current = t
  }, [t])

  const viewsRef = useRef<PhotoViews>(emptyViews())
  const [viewsVersion, setViewsVersion] = useState(0)
  const [uploads, setUploads] = useState<PhotoUploadItem[]>([])
  const [favorites, setFavoritesFilter] = useState(false)
  const [customerId, setCustomerIdState] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notFound, setNotFound] = useState(false)

  const favoritesRef = useRef(false)
  const customerIdRef = useRef<number | null>(null)
  const mounted = useRef(true)

  /** Descarta a resposta de uma leitura superada (filtro trocado, rolagem, renovação). */
  const listToken = useRef(0)
  const loadingMoreRef = useRef(false)
  const lastRequest = useRef<{ owner: number; filter: boolean } | null>(null)

  /** Arquivos e miniaturas locais ficam fora do estado: só o id da fila circula. */
  const uploadItems = useRef(new Map<string, PhotoUploadItem>())
  const uploadSeq = useRef(0)
  const pendingUploads = useRef<string[]>([])
  const activeUploads = useRef(0)

  const sync = useCallback((): void => {
    setViewsVersion((version) => version + 1)
  }, [])

  useEffect(() => {
    mounted.current = true

    // O `Map` é sempre o mesmo objeto (só se faz `clear`), então capturá-lo aqui
    // é seguro: a limpeza não depende de um `.current` que possa ter trocado.
    const items = uploadItems.current

    return () => {
      mounted.current = false
      listToken.current += 1

      // Edge case "envio interrompido": a célula provisória some com a tela e o
      // blobURL é revogado — ou a foto aparece completa no servidor, ou nada fica.
      items.forEach((item) => {
        URL.revokeObjectURL(item.previewUrl)
      })
      items.clear()
      pendingUploads.current = []
      activeUploads.current = 0
    }
  }, [])

  const clearError = useCallback((): void => {
    setError(null)
  }, [])

  /** Uma leitura de página. Erro já traduzido para o estado; nada de toast aqui. */
  const fetchPage = useCallback(
    async (owner: number, filter: boolean, cursor: number | null): Promise<PageOutcome> => {
      try {
        const page = await customerPhotoService.list({
          customerId: owner,
          cursor,
          favorites: filter,
        })

        return { status: 'ok', page }
      } catch (err) {
        if (!mounted.current || err instanceof UnauthorizedError) return { status: 'skip' }

        // 404 = cliente sumida ou de outra vendedora: `notFound` tem tela própria.
        if (err instanceof NotFoundError) {
          setNotFound(true)
          setError(null)

          return { status: 'skip' }
        }

        setNotFound(false)
        setError(toUserMessage(err, translateRef.current))

        return { status: 'error', error: err }
      }
    },
    [],
  )

  const runFirstPage = useCallback(
    async (owner: number, filter: boolean): Promise<void> => {
      listToken.current += 1
      const token = listToken.current
      const key = keyOf(filter)

      setLoading(true)
      setError(null)

      const outcome = await fetchPage(owner, filter, null)

      // Leitura mais nova assumiu o lugar dela: quem chegou depois desliga o `loading`.
      if (!mounted.current || token !== listToken.current) return

      if (outcome.status !== 'ok') {
        setLoading(false)

        if (outcome.status === 'error') {
          notifyError(outcome.error, translateRef.current, () => {
            void runFirstPageRef.current(owner, filter)
          })
        }

        // Falhou: o filtro não pode continuar se passando por carregado.
        viewsRef.current = { ...viewsRef.current, [key]: emptyView() }
        sync()

        return
      }

      viewsRef.current = {
        ...viewsRef.current,
        [key]: {
          pages: [{ cursor: null, items: outcome.page.items }],
          nextCursor: outcome.page.nextCursor,
          loaded: true,
        },
      }

      setNotFound(false)
      setLoading(false)
      sync()
    },
    [fetchPage, sync],
  )

  const runFirstPageRef = useRef(runFirstPage)

  useEffect(() => {
    runFirstPageRef.current = runFirstPage
  }, [runFirstPage])

  const loadPhotos = useCallback(
    async (id: number, options: { favorites?: boolean } = {}): Promise<void> => {
      const filter = options.favorites ?? favoritesRef.current

      // Trocou de cliente: as posições dos dois filtros e a fila vão embora junto.
      if (customerIdRef.current !== id) {
        viewsRef.current = emptyViews()
        pendingUploads.current = []
        uploadItems.current.forEach((item) => {
          URL.revokeObjectURL(item.previewUrl)
        })
        uploadItems.current.clear()
        setUploads([])
        sync()
      }

      customerIdRef.current = id
      favoritesRef.current = filter
      lastRequest.current = { owner: id, filter }

      setCustomerIdState(id)
      setFavoritesFilter(filter)

      await runFirstPage(id, filter)
    },
    [runFirstPage, sync],
  )

  const setFavorites = useCallback(
    async (filter: boolean): Promise<void> => {
      favoritesRef.current = filter
      setFavoritesFilter(filter)

      const owner = customerIdRef.current
      if (owner === null) return

      lastRequest.current = { owner, filter }

      // O filtro já tem páginas na memória: retoma a posição, sem pedir de novo.
      if (viewsRef.current[keyOf(filter)].loaded) {
        listToken.current += 1
        setLoading(false)

        return
      }

      await runFirstPage(owner, filter)
    },
    [runFirstPage],
  )

  const loadMore = useCallback(async (): Promise<void> => {
    const owner = customerIdRef.current
    if (owner === null || loadingMoreRef.current) return

    const key = keyOf(favoritesRef.current)
    const cursor = viewsRef.current[key].nextCursor
    if (cursor === null) return

    listToken.current += 1
    const token = listToken.current

    loadingMoreRef.current = true
    setLoadingMore(true)

    const outcome = await fetchPage(owner, favoritesRef.current, cursor)

    if (!mounted.current || token !== listToken.current) return

    loadingMoreRef.current = false
    setLoadingMore(false)

    if (outcome.status !== 'ok') return

    const view = viewsRef.current[key]

    viewsRef.current = {
      ...viewsRef.current,
      [key]: {
        pages: [...view.pages, { cursor, items: outcome.page.items }],
        nextCursor: outcome.page.nextCursor,
        loaded: true,
      },
    }
    sync()
  }, [fetchPage, sync])

  /**
   * Relê, pelo MESMO cursor, toda página com link vencendo (research R10): a
   * vendedora que deixa o perfil aberto até os links expirarem vê as fotos
   * voltarem sem recarregar a página (edge case "link temporário vencido").
   */
  const ensureFresh = useCallback(async (): Promise<void> => {
    const owner = customerIdRef.current
    if (owner === null) return

    const key = keyOf(favoritesRef.current)
    const stale = viewsRef.current[key].pages.filter((page) => pageIsStale(page, Date.now()))
    if (stale.length === 0) return

    listToken.current += 1
    const token = listToken.current

    const reread = await Promise.all(
      stale.map(async (page): Promise<PageRecord | null> => {
        const outcome = await fetchPage(owner, favoritesRef.current, page.cursor)

        return outcome.status === 'ok' ? { cursor: page.cursor, items: outcome.page.items } : null
      }),
    )

    if (!mounted.current || token !== listToken.current) return

    const replacements = reread.filter((entry): entry is PageRecord => entry !== null)
    if (replacements.length === 0) return

    const view = viewsRef.current[key]

    viewsRef.current = {
      ...viewsRef.current,
      [key]: {
        ...view,
        pages: view.pages.map(
          (page) => replacements.find((candidate) => candidate.cursor === page.cursor) ?? page,
        ),
      },
    }
    sync()
  }, [fetchPage, sync])

  const patchUpload = useCallback(
    (localId: string, state: PhotoUploadState, message: string | null): void => {
      setUploads((prev) =>
        prev.map((entry) => (entry.localId === localId ? { ...entry, state, error: message } : entry)),
      )
    },
    [],
  )

  const dropUpload = useCallback((localId: string): void => {
    const item = uploadItems.current.get(localId)
    if (item === undefined) return

    URL.revokeObjectURL(item.previewUrl)
    uploadItems.current.delete(localId)
    pendingUploads.current = pendingUploads.current.filter((id) => id !== localId)
    setUploads((prev) => prev.filter((entry) => entry.localId !== localId))
  }, [])

  /**
   * Os dois passos de um arquivo (research R7, US2-1): bytes por `POST /images`
   * (validação da 004 intacta) e a linha do álbum por `POST .../photos`, sem
   * descrição e sem perguntas.
   */
  const runUpload = useCallback(
    async (localId: string): Promise<void> => {
      const item = uploadItems.current.get(localId)
      const owner = customerIdRef.current

      if (item === undefined || owner === null) {
        dropUpload(localId)

        return
      }

      try {
        patchUpload(localId, 'uploading', null)

        const image = await imageService.upload(item.file)

        patchUpload(localId, 'attaching', null)

        const result = await customerPhotoService.attach(owner, image.imageId)

        if (result.created) {
          // Entra no topo da grade (US2-1). No filtro "Favoritas", só aparece foto
          // favorita — a foto recém-incluída nunca é, então ali ela é descartada.
          const key = keyOf(favoritesRef.current)

          if (!favoritesRef.current || result.photo.isFavorite) {
            viewsRef.current = prependPhoto(viewsRef.current, key, result.photo)
            sync()
          }
        } else {
          // `created: false` = já estava no álbum (FR-006, US2-5): a célula some e
          // o aviso explica; nada é duplicado.
          toast.warning(translateRef.current('customerPhotos.alreadyInAlbum'))
        }

        dropUpload(localId)
      } catch (err) {
        if (!mounted.current) return

        // 401: o `AuthContext` desloga e avisa; um segundo aviso seria ruído.
        if (err instanceof UnauthorizedError) {
          dropUpload(localId)

          return
        }

        // O motivo vem da 004 no primeiro passo e desta feature no segundo
        // (contracts/ui.md §3); as outras fotos seguem (US2-3).
        patchUpload(localId, 'error', toUserMessage(err, translateRef.current))
      }
    },
    [dropUpload, patchUpload, sync],
  )

  /** Pool de até `PHOTO_UPLOAD_CONCURRENCY` envios simultâneos (research R7). */
  const pump = useCallback((): void => {
    while (activeUploads.current < PHOTO_UPLOAD_CONCURRENCY && pendingUploads.current.length > 0) {
      const localId = pendingUploads.current.shift()
      if (localId === undefined) break

      activeUploads.current += 1

      const release = (): void => {
        activeUploads.current -= 1
        pumpRef.current()
      }

      void runUpload(localId).then(release, release)
    }
  }, [runUpload])

  const pumpRef = useRef(pump)

  useEffect(() => {
    pumpRef.current = pump
  }, [pump])

  /**
   * Coloca os arquivos na fila com a miniatura local (`URL.createObjectURL`), que
   * é revogada quando o item sai (contratos/ui.md §3). A recusa local de tipo e
   * tamanho aconteceu antes, na área de inclusão.
   */
  const enqueueFiles = useCallback(
    (files: File[]): void => {
      if (files.length === 0 || customerIdRef.current === null) return

      const created: PhotoUploadItem[] = files.map((file) => {
        uploadSeq.current += 1

        return {
          localId: `u${String(uploadSeq.current)}`,
          fileName: file.name,
          previewUrl: URL.createObjectURL(file),
          file,
          state: 'uploading',
          error: null,
        }
      })

      created.forEach((item) => {
        uploadItems.current.set(item.localId, item)
        pendingUploads.current.push(item.localId)
      })

      setUploads((prev) => [...prev, ...created])
      pumpRef.current()
    },
    [],
  )

  const retryUpload = useCallback(
    (localId: string): void => {
      if (!uploadItems.current.has(localId) || pendingUploads.current.includes(localId)) return

      pendingUploads.current.unshift(localId)
      patchUpload(localId, 'uploading', null)
      pumpRef.current()
    },
    [patchUpload],
  )

  /**
   * Coração otimista (SC-008): aplica na hora, grava e aplica a resposta por cima.
   * Recusou, estorna e avisa — o estado da grade nunca fica diferente do servidor.
   */
  const toggleFavorite = useCallback(
    async (photo: CustomerPhotoInfo): Promise<CustomerPhotoInfo | null> => {
      const owner = customerIdRef.current
      if (owner === null) return null

      const next = !photo.isFavorite

      viewsRef.current = setFavorite(viewsRef.current, photo.photoId, next)
      sync()

      try {
        const saved = next
          ? await customerPhotoService.favorite(owner, photo.photoId)
          : await customerPhotoService.unfavorite(owner, photo.photoId)

        viewsRef.current = applyPhoto(setFavorite(viewsRef.current, saved.photoId, saved.isFavorite), saved)
        sync()

        return saved
      } catch (err) {
        viewsRef.current = setFavorite(viewsRef.current, photo.photoId, photo.isFavorite)
        sync()

        if (mounted.current && !(err instanceof UnauthorizedError)) {
          toast.error(translateRef.current('customerPhotos.favoriteError'))
        }

        return null
      }
    },
    [sync],
  )

  const updateDescription = useCallback(
    async (photoId: number, description: string | null): Promise<CustomerPhotoInfo | null> => {
      const owner = customerIdRef.current
      if (owner === null) return null

      try {
        const saved = await customerPhotoService.setDescription(owner, photoId, description)

        viewsRef.current = applyPhoto(viewsRef.current, saved)
        sync()

        return saved
      } catch (err) {
        if (mounted.current && !(err instanceof UnauthorizedError)) {
          toast.error(toUserMessage(err, translateRef.current))
        }

        return null
      }
    },
    [sync],
  )

  const removePhoto = useCallback(
    async (photoId: number): Promise<boolean> => {
      const owner = customerIdRef.current
      if (owner === null) return false

      try {
        await customerPhotoService.remove(owner, photoId)

        // Excluir a foto de origem limpa o perfil no servidor (FR-015); o avatar é
        // o `profilePhoto` do detalhe, que a página relê depois daqui.
        viewsRef.current = dropPhoto(viewsRef.current, photoId)
        sync()

        return true
      } catch (err) {
        if (mounted.current && !(err instanceof UnauthorizedError)) {
          toast.error(toUserMessage(err, translateRef.current))
        }

        return false
      }
    },
    [sync],
  )

  /**
   * `PUT .../profile-photo` com o recorte já enviado. A regra de perfil é do
   * servidor: aqui só se marca a foto como origem e se tira das demais (FR-011).
   */
  const setProfilePhoto = useCallback(
    async (photoId: number, imageId?: number): Promise<CustomerProfilePhotoInfo | null> => {
      const owner = customerIdRef.current
      if (owner === null) return null

      try {
        const profile = await customerPhotoService.setProfile(owner, photoId, imageId)

        viewsRef.current = setProfile(viewsRef.current, photoId)
        sync()

        return profile
      } catch (err) {
        // Falha em qualquer um dos dois passos deixa o editor aberto com o motivo
        // (contracts/ui.md §5): nenhum estado de perfil mudou antes do `PUT`.
        if (mounted.current && !(err instanceof UnauthorizedError)) {
          toast.error(toUserMessage(err, translateRef.current))
        }

        return null
      }
    },
    [sync],
  )

  const clearProfilePhoto = useCallback(async (): Promise<boolean> => {
    const owner = customerIdRef.current
    if (owner === null) return false

    try {
      await customerPhotoService.removeProfile(owner)

      viewsRef.current = mapAll(viewsRef.current, (item) =>
        item.isProfile ? { ...item, isProfile: false } : item,
      )
      sync()

      return true
    } catch (err) {
      if (mounted.current && !(err instanceof UnauthorizedError)) {
        toast.error(toUserMessage(err, translateRef.current))
      }

      return false
    }
  }, [sync])

  const retryPhotos = useCallback((): void => {
    const request = lastRequest.current
    if (request === null) return

    setError(null)
    void runFirstPageRef.current(request.owner, request.filter)
  }, [])

  const clearPhotos = useCallback((): void => {
    listToken.current += 1
    loadingMoreRef.current = false
    customerIdRef.current = null
    favoritesRef.current = false
    lastRequest.current = null

    viewsRef.current = emptyViews()
    pendingUploads.current = []

    uploadItems.current.forEach((item) => {
      URL.revokeObjectURL(item.previewUrl)
    })
    uploadItems.current.clear()

    setUploads([])
    setCustomerIdState(null)
    setFavoritesFilter(false)
    setLoading(false)
    setLoadingMore(false)
    setError(null)
    setNotFound(false)
    sync()
  }, [sync])

  const viewKey = keyOf(favorites)

  /**
   * A grade vista. `viewsVersion` não aparece na expressão de propósito: é o
   * aviso de que o `ref` das páginas acumuladas mudou (cursor, prepend, favorita)
   * — sem ele o React não releria o `ref`.
   */
  const view = useMemo(
    () => ({
      photos: itemsOf(viewsRef.current[viewKey]),
      hasMore: viewsRef.current[viewKey].nextCursor !== null,
    }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [viewKey, viewsVersion],
  )

  const value = useMemo<CustomerPhotoContextType>(
    () => ({
      customerId,
      photos: view.photos,
      uploads,
      favorites,
      loading,
      loadingMore,
      hasMore: view.hasMore,
      error,
      notFound,
      loadPhotos,
      setFavorites,
      loadMore,
      ensureFresh,
      enqueueFiles,
      retryUpload,
      dismissUpload: dropUpload,
      toggleFavorite,
      updateDescription,
      removePhoto,
      setProfilePhoto,
      clearProfilePhoto,
      retryPhotos,
      clearPhotos,
      clearError,
    }),
    [
      customerId,
      view,
      uploads,
      favorites,
      loading,
      loadingMore,
      error,
      notFound,
      loadPhotos,
      setFavorites,
      loadMore,
      ensureFresh,
      enqueueFiles,
      retryUpload,
      dropUpload,
      toggleFavorite,
      updateDescription,
      removePhoto,
      setProfilePhoto,
      clearProfilePhoto,
      retryPhotos,
      clearPhotos,
      clearError,
    ],
  )

  return <CustomerPhotoContext.Provider value={value}>{children}</CustomerPhotoContext.Provider>
}

export default CustomerPhotoContext
