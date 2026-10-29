import { useCallback, useEffect, useMemo, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ReceiptText, Images } from 'lucide-react'
import { toast } from 'sonner'

import { BackLink } from '@/components/common/BackLink'
import { EmptyState } from '@/components/common/EmptyState'
import { ErrorState } from '@/components/common/ErrorState'
import { LoadingList } from '@/components/common/LoadingList'
import { NotFound } from '@/components/common/NotFound'
import { Pagination } from '@/components/common/Pagination'
import { CpfReveal } from '@/components/customers/CpfReveal'
import { CustomerAvatar } from '@/components/customers/CustomerAvatar'
import { CustomerSummary } from '@/components/customers/CustomerSummary'
import { CustomerPhotoGrid } from '@/components/customerPhotos/CustomerPhotoGrid'
import { CustomerPhotoViewer } from '@/components/customerPhotos/CustomerPhotoViewer'
import { ProfilePhotoCropper } from '@/components/customerPhotos/ProfilePhotoCropper'
import { OrderCard } from '@/components/orders/OrderCard'
import { Button } from '@/components/ui/button'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useCustomer } from '@/hooks/useCustomer'
import { useCustomerPhotos } from '@/hooks/useCustomerPhotos'
import { formatDateTime } from '@/lib/dates'
import { notifyError, toUserMessage } from '@/lib/errors'
import { parsePage } from '@/lib/filters'
import { NotFoundError, UnauthorizedError } from '@/Services/apiClient'
import { orderService } from '@/Services/orderService'

import type { OrderInfo } from '@/types/order'
import type { CustomerPhotoInfo, CustomerProfilePhotoInfo } from '@/types/customerPhoto'
import { PAGE_SIZE } from '@/types/common'

/** Abas do perfil (FR-017): `?aba=pedidos`; sem parâmetro, Fotos. */
const TAB_PARAM = 'aba'
const PHOTOS_TAB = 'fotos'
const ORDERS_TAB = 'pedidos'

const tabFrom = (value: string | null): string => (value === ORDERS_TAB ? ORDERS_TAB : PHOTOS_TAB)

/**
 * Célula provisória para o caso em que a foto de perfil NÃO está nas páginas já
 * carregadas do álbum (perfil antigo, grade paginada). Não existe rota avulsa de
 * foto — research R10 —, então o que dá para saber do arquivo vem do próprio
 * `profilePhoto`: o `originalImageId` renova o link por `GET /images/{imageId}`.
 * Sem `createdAt` (a tela omite a data) nem descrição: nada é inventado.
 */
const seedFromProfile = (
  profile: CustomerProfilePhotoInfo,
  customerId: number,
): CustomerPhotoInfo => ({
  photoId: profile.photoId,
  customerId,
  imageId: profile.originalImageId,
  description: null,
  isFavorite: false,
  isProfile: true,
  source: 'web',
  createdAt: '',
  updatedAt: '',
  url: profile.originalUrl ?? profile.url,
  urlExpiresAt: profile.urlExpiresAt,
})

/**
 * Perfil da cliente (US2 da 002 + abas da 009).
 *
 * Duas chamadas independentes continuam: o detalhe vem do `CustomerContext` (com
 * o CPF completo, que fica só em memória) e a lista de pedidos do `orderService`
 * com `customerId` — estado LOCAL, porque é a consulta desta tela. O conteúdo de
 * pedidos é o MESMO de antes, apenas dentro da segunda aba (FR-017); os
 * parâmetros que a lista já usa (`pagina`) seguem valendo ali.
 *
 * A aba vai na URL (`?aba=pedidos`, research R9) para o link do perfil abrir no
 * lugar onde a vendedora deixou, e Fotos é a padrão sem parâmetro (FR-017).
 *
 * O álbum vive no `CustomerPhotoContext`: a grade é cursor acumulado, e o que a
 * tela faz aqui é ligar os eventos do contexto aos componentes da feature.
 *
 * `notFound` desenha a MESMA tela para id inexistente e para cliente de outra
 * vendedora (FR-027): nada aqui distingue os dois casos.
 */
export const CustomerProfilePage = () => {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const [searchParams, setSearchParams] = useSearchParams()

  const { selectedCustomer, loading, error, notFound, loadCustomer, retryCustomer, clearSelected } =
    useCustomer()

  const {
    photos,
    uploads,
    favorites,
    loading: photosLoading,
    loadingMore,
    hasMore,
    error: photosError,
    loadPhotos,
    setFavorites,
    loadMore,
    ensureFresh,
    enqueueFiles,
    retryUpload,
    dismissUpload,
    toggleFavorite,
    updateDescription,
    removePhoto,
    setProfilePhoto,
    clearProfilePhoto,
    retryPhotos,
    clearPhotos,
  } = useCustomerPhotos()

  const page = parsePage(searchParams)
  const tab = tabFrom(searchParams.get(TAB_PARAM))

  const [orders, setOrders] = useState<OrderInfo[]>([])
  const [ordersTotal, setOrdersTotal] = useState(0)
  const [ordersLoading, setOrdersLoading] = useState(false)
  const [ordersError, setOrdersError] = useState<string | null>(null)

  const [viewerIndex, setViewerIndex] = useState<number | null>(null)
  const [viewerOpen, setViewerOpen] = useState(false)
  /** Foto de perfil fora das páginas carregadas: a célula única do fallback. */
  const [viewerSeed, setViewerSeed] = useState<CustomerPhotoInfo | null>(null)
  const [cropPhoto, setCropPhoto] = useState<CustomerPhotoInfo | null>(null)

  const numericId = id === undefined ? Number.NaN : Number(id)
  const validId = Number.isInteger(numericId) && numericId > 0

  useEffect(() => {
    if (!validId) {
      clearSelected()

      return
    }

    void loadCustomer(numericId)
  }, [validId, numericId, loadCustomer, clearSelected])

  /** O álbum da cliente aberta; saiu da tela, as páginas e a fila vão embora. */
  useEffect(() => {
    if (!validId) {
      clearPhotos()

      return
    }

    void loadPhotos(numericId)
  }, [clearPhotos, loadPhotos, numericId, validId])

  /** Saiu da tela: o CPF completo vai embora com o componente (FR-022). */
  useEffect(() => {
    return () => {
      clearSelected()
    }
  }, [clearSelected])

  const loadOrders = useCallback(async (): Promise<void> => {
    if (!validId) return

    setOrdersLoading(true)
    setOrdersError(null)

    try {
      const result = await orderService.list({
        customerId: numericId,
        page,
        pageSize: PAGE_SIZE,
      })
      setOrders(result.items)
      setOrdersTotal(result.totalCount)
    } catch (err) {
      setOrders([])
      setOrdersTotal(0)

      // 404 aqui é o cliente sumindo entre as duas chamadas: a tela "não
      // encontrado" do perfil já cobre. 401 pertence ao AuthContext.
      if (err instanceof NotFoundError || err instanceof UnauthorizedError) return

      setOrdersError(toUserMessage(err, t))
      // FR-026 (T086): o toast repete a consulta desta tela.
      notifyError(err, t, () => {
        void loadOrders()
      })
    } finally {
      setOrdersLoading(false)
    }
  }, [validId, numericId, page, t])

  useEffect(() => {
    void loadOrders()
  }, [loadOrders])

  const changePage = useCallback(
    (target: number): void => {
      const next = new URLSearchParams(searchParams)
      next.set('pagina', String(target))
      setSearchParams(next)
    },
    [searchParams, setSearchParams],
  )

  const backToFirstPage = useCallback((): void => {
    const next = new URLSearchParams(searchParams)
    next.delete('pagina')
    setSearchParams(next, { replace: true })
  }, [searchParams, setSearchParams])

  const retryOrders = useCallback((): void => {
    void loadOrders()
  }, [loadOrders])

  /** Aba na URL: `?aba=pedidos`; "Fotos" volta a limpar o parâmetro (research R9). */
  const changeTab = useCallback(
    (value: string): void => {
      const next = new URLSearchParams(searchParams)

      if (value === ORDERS_TAB) next.set(TAB_PARAM, ORDERS_TAB)
      else next.delete(TAB_PARAM)

      setSearchParams(next)
    },
    [searchParams, setSearchParams],
  )

  /** Lista que o visualizador percorre: o álbum, com o fallback do perfil na frente. */
  const viewerPhotos = useMemo<CustomerPhotoInfo[]>(
    () =>
      viewerSeed === null
        ? photos
        : [viewerSeed, ...photos.filter((photo) => photo.photoId !== viewerSeed.photoId)],
    [photos, viewerSeed],
  )

  const openPhoto = useCallback(
    (photo: CustomerPhotoInfo): void => {
      // Abrir pode exigir links que venceram com a tela parada: relê a página
      // pelo mesmo cursor antes de mostrar (research R10, edge case "vencido").
      void ensureFresh()

      const at = photos.findIndex((entry) => entry.photoId === photo.photoId)
      if (at < 0) return

      setViewerSeed(null)
      setViewerIndex(at)
      setViewerOpen(true)
    },
    [ensureFresh, photos],
  )

  /**
   * Toque no círculo do cabeçalho abre a ORIGINAL de onde o perfil saiu
   * (FR-014, US3-4). Quando a foto está além das páginas carregadas, abre com a
   * célula única montada do `profilePhoto` — sem rota avulsa de foto (R10).
   */
  const openProfileOriginal = useCallback((): void => {
    const profile = selectedCustomer?.profilePhoto
    if (profile === null || profile === undefined) return

    const at = photos.findIndex((entry) => entry.photoId === profile.photoId)

    if (at >= 0) {
      setViewerSeed(null)
      setViewerIndex(at)
      setViewerOpen(true)

      return
    }

    const customerId = selectedCustomer?.id
    if (customerId === undefined) return

    setViewerSeed(seedFromProfile(profile, customerId))
    setViewerIndex(0)
    setViewerOpen(true)
  }, [photos, selectedCustomer])

  /** "Ajustar foto de perfil" do cabeçalho: reabre o editor sobre o original (US3-8). */
  const openProfileCropper = useCallback((): void => {
    const profile = selectedCustomer?.profilePhoto
    if (profile === null || profile === undefined) return

    const customerId = selectedCustomer?.id
    if (customerId === undefined) return

    setCropPhoto(
      photos.find((entry) => entry.photoId === profile.photoId) ??
        seedFromProfile(profile, customerId),
    )
  }, [photos, selectedCustomer])

  /**
   * Perfil gravado: o avatar do cabeçalho vem do DETALHE (`profilePhoto`), então
   * a tela relê o detalhe. A lista de clientes se atualiza sozinha ao voltar
   * (ela recarrega no montar), e nada aqui esconde estado velho (US3-2/US3-3).
   */
  const applyProfile = useCallback(
    async (photo: CustomerPhotoInfo, imageId: number): Promise<CustomerProfilePhotoInfo | null> => {
      const profile = await setProfilePhoto(photo.photoId, imageId)

      if (profile === null) return null

      toast.success(t('customerPhotos.profileSaved'))
      void loadCustomer(numericId)

      return profile
    },
    [loadCustomer, numericId, setProfilePhoto, t],
  )

  const dropProfile = useCallback(async (): Promise<boolean> => {
    const removed = await clearProfilePhoto()

    if (!removed) return false

    toast.success(t('customerPhotos.profileRemoved'))
    void loadCustomer(numericId)

    return true
  }, [clearProfilePhoto, loadCustomer, numericId, t])

  /**
   * Excluir a foto de origem também tira o perfil (FR-015, US3-6): por isso o
   * detalhe é relido depois do 204, e o círculo volta às iniciais.
   */
  const deletePhoto = useCallback(
    async (photo: CustomerPhotoInfo): Promise<boolean> => {
      const removed = await removePhoto(photo.photoId)

      if (removed && photo.isProfile) void loadCustomer(numericId)

      return removed
    },
    [loadCustomer, numericId, removePhoto],
  )

  const saveDescription = useCallback(
    async (photoId: number, description: string | null): Promise<boolean> =>
      (await updateDescription(photoId, description)) !== null,
    [updateDescription],
  )

  const changeFavorites = useCallback(
    (value: boolean): void => {
      void setFavorites(value)
    },
    [setFavorites],
  )

  const profilePhoto = selectedCustomer?.profilePhoto ?? null

  if (!validId || notFound) {
    return (
      <NotFound
        title={t('customers.notFound')}
        linkTo="/clientes"
        linkLabel={t('customers.backToList')}
      />
    )
  }

  if (loading && selectedCustomer === null) {
    return <LoadingList count={3} />
  }

  if (error !== null && selectedCustomer === null) {
    return <ErrorState message={error} onRetry={retryCustomer} />
  }

  if (selectedCustomer === null) return null

  const ordersEmpty = !ordersLoading && orders.length === 0
  const beyondEnd = ordersEmpty && ordersTotal > 0

  const since = formatDateTime(selectedCustomer.createdAt).slice(0, 10)

  /** Perfil sem recorte dedicado (canal sem navegador): a vendedora é avisada (US3-9). */
  const withoutDedicatedFrame =
    profilePhoto !== null && profilePhoto.imageId === profilePhoto.originalImageId

  const headerAvatar = (
    <CustomerAvatar
      name={selectedCustomer.name}
      photoUrl={profilePhoto?.url}
      size="xl"
    />
  )

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/clientes" label={t('common.customers')} />

      {/* Cabeçalho de perfil (estilo rede social): avatar grande com anel no
          gradiente da marca, nome, CPF e "Cliente desde". Com foto de perfil, o
          círculo é botão e abre a original (FR-014); sem ela, são as iniciais e
          nada é clicável (US3-5). */}
      <header className="relative isolate flex min-w-0 flex-col items-center gap-3 text-center">
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-x-0 -top-10 -z-10 mx-auto h-48 max-w-md rounded-full bg-brand/25 blur-3xl"
        />
        <div className="rounded-full bg-gradient-to-tr from-brand via-brand-soft to-brand-cyan p-[3px]">
          <div className="rounded-full bg-background p-[3px]">
            {profilePhoto === null ? (
              headerAvatar
            ) : (
              <button
                type="button"
                className="block rounded-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand"
                aria-label={t('customerPhotos.viewOriginal', { name: selectedCustomer.name })}
                onClick={openProfileOriginal}
                data-testid="profile-avatar-button"
              >
                {headerAvatar}
              </button>
            )}
          </div>
        </div>
        <div className="flex w-full min-w-0 flex-col items-center gap-1">
          <h1 className="w-full truncate text-2xl font-bold tracking-tight text-foreground md:text-3xl">
            {selectedCustomer.name}
          </h1>
          <div className="flex justify-center">
            <CpfReveal cpf={selectedCustomer.cpf} cpfMasked={selectedCustomer.cpfMasked} />
          </div>
          {since === '' ? null : (
            <p className="text-xs text-muted-foreground tabular-nums">
              {t('customers.since', { date: since })}
            </p>
          )}
        </div>

        {profilePhoto === null ? null : (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="min-h-11"
            onClick={openProfileCropper}
            data-testid="adjust-profile"
          >
            {t('customerPhotos.adjustProfile')}
          </Button>
        )}

        {withoutDedicatedFrame ? (
          <p className="max-w-sm text-xs text-muted-foreground" data-testid="profile-without-frame">
            {t('customerPhotos.profileWithoutFrame')}
          </p>
        ) : null}
      </header>

      <section aria-label={t('customers.summaryAriaLabel')} className="mx-auto w-full max-w-2xl">
        <CustomerSummary summary={selectedCustomer.summary} />
      </section>

      {/* FR-017: Fotos (padrão) e Pedidos, no `Tabs` da research R9. A aba vai na
          URL; o bloco de pedidos é o mesmo de antes, agora dentro da segunda aba. */}
      <Tabs
        value={tab}
        onValueChange={changeTab}
        className="mx-auto w-full max-w-3xl"
        aria-label={t('customerPhotos.tabsAriaLabel')}
      >
        <TabsList className="w-full justify-start">
          <TabsTrigger value={PHOTOS_TAB} data-testid="tab-photos">
            <Images aria-hidden="true" />
            {t('customerPhotos.tabPhotos')}
          </TabsTrigger>
          <TabsTrigger value={ORDERS_TAB} data-testid="tab-orders">
            <ReceiptText aria-hidden="true" />
            {t('customerPhotos.tabOrders')}
            {ordersTotal > 0 ? (
              <span className="rounded-full bg-secondary px-2 py-0.5 text-xs font-medium text-muted-foreground tabular-nums">
                {ordersTotal}
              </span>
            ) : null}
          </TabsTrigger>
        </TabsList>

        <TabsContent value={PHOTOS_TAB} className="flex flex-col gap-2">
          <CustomerPhotoGrid
            customerName={selectedCustomer.name}
            photos={photos}
            uploads={uploads}
            favorites={favorites}
            loading={photosLoading}
            loadingMore={loadingMore}
            hasMore={hasMore}
            error={photosError}
            onFavoritesChange={changeFavorites}
            onOpen={openPhoto}
            onLoadMore={() => {
              void loadMore()
            }}
            onFiles={enqueueFiles}
            onRetryUpload={retryUpload}
            onDismissUpload={dismissUpload}
            onRetry={retryPhotos}
          />
        </TabsContent>

        <TabsContent value={ORDERS_TAB} className="flex flex-col gap-3">
          {ordersLoading ? <LoadingList count={3} /> : null}

          {!ordersLoading && ordersError !== null ? (
            <ErrorState message={ordersError} onRetry={retryOrders} />
          ) : null}

          {ordersEmpty ? (
            <EmptyState
              title={t('orders.empty')}
              {...(beyondEnd
                ? { actionLabel: t('common.backToFirstPage'), onAction: backToFirstPage }
                : {})}
            />
          ) : null}

          {!ordersLoading && orders.length > 0 ? (
            <>
              <ul role="list" className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {orders.map((order) => (
                  <li key={order.id} className="min-w-0">
                    <OrderCard order={order} hideCustomer />
                  </li>
                ))}
              </ul>
              <Pagination
                page={page}
                pageSize={PAGE_SIZE}
                totalCount={ordersTotal}
                onPageChange={changePage}
              />
            </>
          ) : null}
        </TabsContent>
      </Tabs>

      <CustomerPhotoViewer
        open={viewerOpen}
        photos={viewerPhotos}
        index={viewerIndex ?? 0}
        onIndexChange={setViewerIndex}
        onClose={() => {
          setViewerOpen(false)
          setViewerIndex(null)
          setViewerSeed(null)
        }}
        customerName={selectedCustomer.name}
        onToggleFavorite={toggleFavorite}
        onUpdateDescription={saveDescription}
        onUseAsProfile={applyProfile}
        onRemoveProfile={dropProfile}
        onDelete={deletePhoto}
        onRequestMore={() => {
          void loadMore()
        }}
      />

      {/* "Ajustar foto de perfil" do cabeçalho, sem passar pelo visualizador. */}
      <ProfilePhotoCropper
        open={cropPhoto !== null}
        onOpenChange={(next) => {
          if (next) return

          setCropPhoto(null)
        }}
        photo={cropPhoto}
        isCurrentProfile
        onSave={(imageId) => {
          const photo = cropPhoto

          return photo === null ? Promise.resolve(null) : applyProfile(photo, imageId)
        }}
        onRemoveProfile={dropProfile}
      />
    </div>
  )
}

export default CustomerProfilePage
