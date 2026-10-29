import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Heart, Loader2 } from 'lucide-react'

import { CustomerPhotoDropzone } from '@/components/customerPhotos/CustomerPhotoDropzone'
import { EmptyState } from '@/components/common/EmptyState'
import { ErrorState } from '@/components/common/ErrorState'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useRenewedUrl } from '@/hooks/useRenewedUrl'
import { formatDateLong } from '@/lib/dates'
import { cn } from '@/lib/utils'

import type { CustomerPhotoInfo, PhotoUploadItem } from '@/types/customerPhoto'
import { PHOTO_GRID_SKELETON_CELLS } from '@/types/customerPhoto'

export interface CustomerPhotoGridProps {
  /** Nome da cliente, para o `alt` e para a frase de álbum vazio. */
  customerName: string
  photos: CustomerPhotoInfo[]
  /** Arquivos da fila, exibidos como células provisórias no topo (FR-021). */
  uploads: PhotoUploadItem[]
  favorites: boolean
  loading: boolean
  loadingMore: boolean
  hasMore: boolean
  error: string | null
  onFavoritesChange: (favorites: boolean) => void
  onOpen: (photo: CustomerPhotoInfo) => void
  onLoadMore: () => void
  onFiles: (files: File[]) => void
  onRetryUpload: (localId: string) => void
  onDismissUpload: (localId: string) => void
  onRetry: () => void
}

/**
 * Grade de fotos do perfil (contracts/ui.md §2, FR-018/FR-022).
 *
 * Três colunas em QUALQUER largura, como num perfil de rede social, `gap-0.5` e
 * célula `aspect-square`: o recorte quadrado é só CSS (`object-cover`) porque
 * **não existe miniatura** — a grade mostra o arquivo original (research R3). O
 * que segura o tráfego é a página de 24 com `loading="lazy"`+`decoding="async"`.
 *
 * Rolagem infinita: uma sentinela no fim pede a próxima página pelo cursor
 * (US1-7). No primeiro carregamento, nove células de `Skeleton` (3×3).
 *
 * Estado vazio (US1-6): a frase com o nome da cliente e a área de inclusão
 * abaixo, sem o destaque discreto de quem já tem fotos.
 */
export const CustomerPhotoGrid = ({
  customerName,
  photos,
  uploads,
  favorites,
  loading,
  loadingMore,
  hasMore,
  error,
  onFavoritesChange,
  onOpen,
  onLoadMore,
  onFiles,
  onRetryUpload,
  onDismissUpload,
  onRetry,
}: CustomerPhotoGridProps) => {
  const { t } = useTranslation()

  const sentinel = useRef<HTMLDivElement>(null)

  /** Sentinela da rolagem infinita (US1-7): perto do fim, pede a próxima página. */
  useEffect(() => {
    const node = sentinel.current
    if (node === null || !hasMore) return

    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) onLoadMore()
    })

    observer.observe(node)

    return () => {
      observer.disconnect()
    }
  }, [hasMore, onLoadMore, photos.length, uploads.length])

  const empty = !loading && error === null && photos.length === 0 && uploads.length === 0

  return (
    <div className="flex flex-col gap-2" data-testid="photo-grid">
      {/* Filtro Todas / Favoritas (FR-010, US4-6): a posição de cada um fica no contexto. */}
      <div
        role="group"
        aria-label={t('customerPhotos.filterAriaLabel')}
        className="flex items-center gap-1"
      >
        <Button
          type="button"
          size="sm"
          variant={favorites ? 'ghost' : 'secondary'}
          className="min-h-11 rounded-full px-3 text-xs"
          aria-pressed={!favorites}
          onClick={() => {
            onFavoritesChange(false)
          }}
        >
          {t('customerPhotos.filterAll')}
        </Button>
        <Button
          type="button"
          size="sm"
          variant={favorites ? 'secondary' : 'ghost'}
          className="min-h-11 gap-1.5 rounded-full px-3 text-xs"
          aria-pressed={favorites}
          onClick={() => {
            onFavoritesChange(true)
          }}
        >
          <Heart aria-hidden="true" className={cn('size-3.5', favorites && 'fill-current')} />
          {t('customerPhotos.filterFavorites')}
        </Button>
      </div>

      <CustomerPhotoDropzone onFiles={onFiles} />

      {loading ? (
        <div
          role="status"
          aria-busy="true"
          aria-label={t('customerPhotos.loading')}
          className="grid grid-cols-3 gap-0.5"
          data-testid="photo-skeleton"
        >
          {Array.from({ length: PHOTO_GRID_SKELETON_CELLS }, (_unused, index) => (
            <Skeleton key={index} className="aspect-square rounded-none" />
          ))}
        </div>
      ) : null}

      {!loading && error !== null ? <ErrorState message={error} onRetry={onRetry} /> : null}

      {empty ? (
        <EmptyState title={t('customerPhotos.empty', { name: customerName })} />
      ) : null}

      {photos.length > 0 || uploads.length > 0 ? (
        <ul
          role="list"
          className="grid min-w-0 grid-cols-3 gap-0.5 overflow-hidden rounded-lg"
          data-testid="photo-cells"
        >
          {/* Fotos em envio entram no TOPO, com a miniatura local escurecida e o
              estado da fila por baixo (FR-021, contracts/ui.md §3). */}
          {uploads.map((item) => (
            <li key={item.localId} className="min-w-0" data-testid={`photo-upload-${item.state}`}>
              <div
                className={cn(
                  'relative aspect-square overflow-hidden bg-muted',
                  item.state === 'error' && 'border-2 border-destructive',
                )}
              >
                <img
                  src={item.previewUrl}
                  alt={item.fileName}
                  aria-hidden="true"
                  className="size-full object-cover opacity-60"
                />
                <div className="absolute inset-x-0 bottom-0 flex flex-col gap-1 bg-background/85 p-1">
                  {item.state === 'error' ? (
                    <>
                      <p className="line-clamp-2 text-[0.625rem] leading-tight text-destructive">
                        {item.error ?? t('customerPhotos.stateError')}
                      </p>
                      <div className="flex gap-1">
                        <Button
                          type="button"
                          size="xs"
                          variant="outline"
                          className="min-h-11 grow text-[0.625rem]"
                          onClick={() => {
                            onRetryUpload(item.localId)
                          }}
                        >
                          {t('common.retry')}
                        </Button>
                        <Button
                          type="button"
                          size="xs"
                          variant="ghost"
                          className="min-h-11 text-[0.625rem]"
                          onClick={() => {
                            onDismissUpload(item.localId)
                          }}
                        >
                          {t('customerPhotos.dismiss')}
                        </Button>
                      </div>
                    </>
                  ) : (
                    <p className="flex items-center gap-1 text-[0.625rem] text-muted-foreground">
                      <Loader2
                        aria-hidden="true"
                        className="size-3 shrink-0 animate-spin motion-reduce:animate-none"
                      />
                      {item.state === 'uploading'
                        ? t('customerPhotos.stateUploading')
                        : t('customerPhotos.stateAttaching')}
                    </p>
                  )}
                </div>
              </div>
            </li>
          ))}

          {photos.map((photo) => (
            <li key={String(photo.photoId)} className="min-w-0">
              <PhotoCell photo={photo} customerName={customerName} onOpen={onOpen} />
            </li>
          ))}

          {loadingMore ? (
            <li className="min-w-0" data-testid="photo-loading-more">
              <div
                role="status"
                aria-label={t('customerPhotos.loadingMore')}
                className="flex aspect-square items-center justify-center bg-muted text-muted-foreground"
              >
                <Loader2
                  aria-hidden="true"
                  className="size-5 animate-spin motion-reduce:animate-none"
                />
              </div>
            </li>
          ) : null}
        </ul>
      ) : null}

      {hasMore ? <div ref={sentinel} aria-hidden="true" className="h-1" data-testid="photo-sentinel" /> : null}
    </div>
  )
}

export interface PhotoCellProps {
  photo: CustomerPhotoInfo
  customerName: string
  onOpen: (photo: CustomerPhotoInfo) => void
}

/**
 * Uma célula da grade: `<button>` com o original em `object-cover` (o corte
 * quadrado é visual, FR-018) e o coração cheio nas favoritas (FR-022). O link
 * vencido renova pelo `GET /images/{imageId}` da 004, uma vez por URL (R10).
 */
const PhotoCell = ({ photo, customerName, onOpen }: PhotoCellProps) => {
  const { t } = useTranslation()

  const { src, onError } = useRenewedUrl(photo.imageId, photo.url)

  const date = formatDateLong(photo.createdAt)
  const alt =
    photo.description !== null && photo.description.trim() !== ''
      ? photo.description
      : t('customerPhotos.photoAlt', { name: customerName, date })

  return (
    <button
      type="button"
      className="relative block aspect-square w-full overflow-hidden bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand"
      onClick={() => {
        onOpen(photo)
      }}
      data-testid="photo-cell"
    >
      <img
        src={src}
        alt={alt}
        loading="lazy"
        decoding="async"
        referrerPolicy="no-referrer"
        className="size-full object-cover"
        onError={onError}
      />
      {photo.isFavorite ? (
        // Só o coração cheio (FR-022): decorativo, porque o `alt` da foto já é o
        // nome da cliente com a data — anunciar "Favorita" em cada célula do list
        // seria ruído no leitor de tela.
        <Heart
          aria-hidden="true"
          data-testid="photo-favorite-mark"
          className="absolute top-1 right-1 size-4 fill-white text-white drop-shadow-md"
        />
      ) : null}
      {photo.isProfile ? (
        <span
          className="absolute bottom-1 left-1 rounded-full bg-background/80 px-1.5 py-0.5 text-[0.5625rem] font-medium text-foreground"
          data-testid="photo-profile-mark"
        >
          {t('customerPhotos.profileBadge')}
        </span>
      ) : null}
    </button>
  )
}

export default CustomerPhotoGrid
