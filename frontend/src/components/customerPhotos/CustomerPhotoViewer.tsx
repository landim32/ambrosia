import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronLeft, ChevronRight, Heart, Trash2 } from 'lucide-react'

import { ConfirmModal } from '@/components/common/ConfirmModal'
import { CustomerPhotoDescription } from '@/components/customerPhotos/CustomerPhotoDescription'
import { Modal } from '@/components/common/Modal'
import { ProfilePhotoCropper } from '@/components/customerPhotos/ProfilePhotoCropper'
import { Button } from '@/components/ui/button'
import { useRenewedUrl } from '@/hooks/useRenewedUrl'
import { formatDateLong } from '@/lib/dates'
import { cn } from '@/lib/utils'

import type { PointerEvent as ReactPointerEvent } from 'react'
import type { CustomerPhotoInfo, CustomerPhotoSource, CustomerProfilePhotoInfo } from '@/types/customerPhoto'

export interface CustomerPhotoViewerProps {
  open: boolean
  /** Fotos na ordem da grade; o visualizador anda por elas (FR-019). */
  photos: CustomerPhotoInfo[]
  index: number
  onIndexChange: (index: number) => void
  onClose: () => void
  customerName: string
  /** Coração otimista do contexto, com estorno e aviso (SC-008). */
  onToggleFavorite: (photo: CustomerPhotoInfo) => Promise<CustomerPhotoInfo | null>
  onUpdateDescription: (photoId: number, description: string | null) => Promise<boolean>
  /** Recorte já enviado por `POST /images`: `PUT .../profile-photo`. */
  onUseAsProfile: (photo: CustomerPhotoInfo, imageId: number) => Promise<CustomerProfilePhotoInfo | null>
  onRemoveProfile: () => Promise<boolean>
  /** `DELETE .../photos/{photoId}` (com a confirmação já acontecida aqui). */
  onDelete: (photo: CustomerPhotoInfo) => Promise<boolean>
  /** Perto do fim da lista carregada, pede a próxima página (US1-4). */
  onRequestMore: () => void
}

/** Deslize horizontal mínimo para virar a foto (contracts/ui.md §4). */
const SWIPE_THRESHOLD_PX = 40

const SOURCE_KEYS: Record<CustomerPhotoSource, string> = {
  web: 'customerPhotos.sourceWeb',
  api: 'customerPhotos.sourceApi',
  chatGpt: 'customerPhotos.sourceChatGpt',
}

/**
 * Visualizador da foto ampliada (contracts/ui.md §4, FR-019) sobre o `Modal`
 * existente (skill `react-modal`) — sem biblioteca de lightbox, que traria o que
 * a spec não pede e duplicaria o diálogo (research R9).
 *
 * Mostra o **original** em `object-contain`, com a proporção real: não há
 * miniatura intermediária (research R3). Navegação por setas ‹ ›, pelas teclas
 * ← → e por deslize no toque; `Esc` fecha (é o `Modal`). Perto do fim da lista
 * carregada, a tela pede a próxima página (US1-4) — o cursor é do contexto.
 *
 * Link vencido: `onError` na `<img>` chama `imageService.getReadUrl(imageId)`
 * uma vez por URL (research R10) — a renovação é a rota `GET /images/{imageId}`
 * da feature 004, que já cacheia e compartilha as prometidas em voo. Não existe
 * rota avulsa de foto, e nada aqui inventa uma.
 */
export const CustomerPhotoViewer = ({
  open,
  photos,
  index,
  onIndexChange,
  onClose,
  customerName,
  onToggleFavorite,
  onUpdateDescription,
  onUseAsProfile,
  onRemoveProfile,
  onDelete,
  onRequestMore,
}: CustomerPhotoViewerProps) => {
  const { t } = useTranslation()

  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [cropping, setCropping] = useState(false)

  const photo = photos[index]

  /** Último índice aberto, para a renovação de URL não depender de closure velha. */
  const current = useRef<CustomerPhotoInfo | null>(null)

  if (photo !== undefined) current.current = photo

  const move = useCallback(
    (delta: number): void => {
      const next = index + delta

      if (next < 0) return

      if (next > photos.length - 1) {
        // Já no fim do que foi carregado: pede a página seguinte e para (US1-4).
        onRequestMore()

        return
      }

      onIndexChange(next)
    },
    [index, onIndexChange, onRequestMore, photos.length],
  )

  // Teclado: ← → navegam enquanto o diálogo está aberto (contracts/ui.md §4).
  useEffect(() => {
    if (!open) return

    const onKey = (event: KeyboardEvent): void => {
      if (event.key === 'ArrowRight') {
        event.preventDefault()
        move(1)

        return
      }

      if (event.key === 'ArrowLeft') {
        event.preventDefault()
        move(-1)
      }
    }

    window.addEventListener('keydown', onKey)

    return () => {
      window.removeEventListener('keydown', onKey)
    }
  }, [move, open])

  // Chegando perto do fim da lista carregada, antecipa a próxima página.
  useEffect(() => {
    if (!open || photos.length === 0) return

    if (index >= photos.length - 2) onRequestMore()
  }, [index, onRequestMore, open, photos.length])

  const swipe = useRef<{ x: number; y: number } | null>(null)

  const onPointerDown = useCallback((event: ReactPointerEvent<HTMLDivElement>): void => {
    swipe.current = { x: event.clientX, y: event.clientY }
  }, [])

  const onPointerUp = useCallback(
    (event: ReactPointerEvent<HTMLDivElement>): void => {
      const start = swipe.current
      swipe.current = null
      if (start === null) return

      const dx = event.clientX - start.x
      const dy = event.clientY - start.y

      // Deslizar é horizontal: um gesto mais vertical é a vendedora rolando a página.
      if (Math.abs(dx) < SWIPE_THRESHOLD_PX || Math.abs(dy) > Math.abs(dx)) return

      move(dx < 0 ? 1 : -1)
    },
    [move],
  )

  const confirmDelete = useCallback(async (): Promise<void> => {
    const target = current.current
    if (target === null) return

    setDeleting(true)

    const removed = await onDelete(target)

    setDeleting(false)
    setConfirmingDelete(false)

    if (!removed) return

    // Mostra a próxima (o item seguinte ocupa o lugar) ou fecha se era a última
    // (contracts/ui.md §4). O contexto já tirou a foto da lista.
    const remaining = photos.length - 1

    if (remaining <= 0) {
      onClose()

      return
    }

    if (index > remaining - 1) onIndexChange(remaining - 1)
  }, [index, onDelete, onIndexChange, onClose, photos.length])

  if (!open || photo === undefined) return null

  return (
    <>
      <Modal
        open={open}
        onOpenChange={(next) => {
          // O ConfirmModal e o cropper têm o próprio fechamento; o Esc aqui só
          // fecha o visualizador quando nada em cima espera resposta.
          if (next) return

          if (confirmingDelete || cropping || deleting) return

          onClose()
        }}
        title={t('customerPhotos.viewerTitle', { name: customerName })}
        closeLabel={t('customerPhotos.close')}
        className="max-w-4xl"
        footer={
          <>
            <Button
              type="button"
              variant="outline"
              className="min-h-11"
              onClick={() => {
                setCropping(true)
              }}
              data-testid="viewer-use-as-profile"
            >
              {photo.isProfile
                ? t('customerPhotos.adjustFrame')
                : t('customerPhotos.useAsProfile')}
            </Button>
            <Button
              type="button"
              variant="destructive"
              className="min-h-11 gap-2"
              onClick={() => {
                setConfirmingDelete(true)
              }}
              data-testid="viewer-delete"
            >
              <Trash2 aria-hidden="true" className="size-4" />
              {t('customerPhotos.delete')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-2">
          <div
            className="relative flex items-center justify-center overflow-hidden rounded-xl bg-muted"
            style={{ touchAction: 'pan-y' }}
            onPointerDown={onPointerDown}
            onPointerUp={onPointerUp}
            data-testid="viewer-stage"
          >
            {/* O original inteiro, na proporção real: nada de recorte aqui (§4, R3). */}
            <ViewerImage photo={photo} customerName={customerName} />

            <button
              type="button"
              aria-label={t('customerPhotos.previous')}
              className="absolute left-1 flex size-11 items-center justify-center rounded-full bg-background/70 text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand disabled:opacity-30"
              disabled={index === 0}
              onClick={() => {
                move(-1)
              }}
              data-testid="viewer-previous"
            >
              <ChevronLeft aria-hidden="true" className="size-5" />
            </button>
            <button
              type="button"
              aria-label={t('customerPhotos.next')}
              className="absolute right-1 flex size-11 items-center justify-center rounded-full bg-background/70 text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand"
              onClick={() => {
                move(1)
              }}
              data-testid="viewer-next"
            >
              <ChevronRight aria-hidden="true" className="size-5" />
            </button>
          </div>

          {/* Coração otimista (FR-008, SC-008) + data + origem da foto. */}
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted-foreground">
            <button
              type="button"
              aria-pressed={photo.isFavorite}
              aria-label={
                photo.isFavorite
                  ? t('customerPhotos.favoriteOn')
                  : t('customerPhotos.favoriteOff')
              }
              className="flex size-11 items-center justify-center rounded-full hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand"
              onClick={() => {
                void onToggleFavorite(photo)
              }}
              data-testid="viewer-favorite"
            >
              <Heart
                aria-hidden="true"
                className={cn(
                  'size-5',
                  photo.isFavorite ? 'fill-destructive text-destructive' : 'text-muted-foreground',
                )}
              />
            </button>

            {/* Data e origem só existem para foto do álbum lida da listagem. A
                célula montada do `profilePhoto` (perfil fora da página carregada)
                não traz `createdAt`, e inventar uma data seria mentira. */}
            {photo.createdAt === '' ? null : (
              <>
                <span className="tabular-nums">{formatDateLong(photo.createdAt)}</span>
                <span aria-hidden="true">·</span>
                <span>{t(SOURCE_KEYS[photo.source])}</span>
              </>
            )}

            {photo.isProfile ? (
              <span className="text-xs" data-testid="viewer-profile-badge">
                {t('customerPhotos.profileBadge')}
              </span>
            ) : null}
          </div>

          <CustomerPhotoDescription
            description={photo.description}
            onSave={(description) => {
              return onUpdateDescription(photo.photoId, description)
            }}
          />
        </div>
      </Modal>

      <ConfirmModal
        open={confirmingDelete}
        onOpenChange={(next) => {
          if (deleting && !next) return

          setConfirmingDelete(next)
        }}
        title={t('customerPhotos.deleteTitle')}
        // FR-015 / US3-6: excluir a foto de origem tira o perfil — dito antes do
        // clique, para a vendedora não descobrir pelo círculo sumindo.
        description={
          photo.isProfile ? t('customerPhotos.deleteProfileExtra', { name: customerName }) : undefined
        }
        confirmLabel={t('customerPhotos.deleteConfirm')}
        cancelLabel={t('customerPhotos.deleteCancel')}
        closeLabel={t('customerPhotos.close')}
        variant="danger"
        busy={deleting}
        busyLabel={t('customerPhotos.deleting')}
        onConfirm={() => {
          void confirmDelete()
        }}
        testId="viewer-delete-confirm"
      />

      <ProfilePhotoCropper
        open={cropping}
        onOpenChange={setCropping}
        photo={photo}
        isCurrentProfile={photo.isProfile}
        onSave={(imageId) => {
          return onUseAsProfile(photo, imageId)
        }}
        onRemoveProfile={onRemoveProfile}
      />
    </>
  )
}

/**
 * A foto em si, separada só porque o `useRenewedUrl` é um hook e precisa de um
 * componente por foto exibida (a URL é por arquivo, research R10).
 */
const ViewerImage = ({
  photo,
  customerName,
}: {
  photo: CustomerPhotoInfo
  customerName: string
}) => {
  const { t } = useTranslation()

  const { src, onError } = useRenewedUrl(photo.imageId, photo.url)

  const date = formatDateLong(photo.createdAt)
  const alt =
    photo.description !== null && photo.description.trim() !== ''
      ? photo.description
      : t('customerPhotos.photoAlt', { name: customerName, date })

  return (
    <img
      src={src}
      alt={alt}
      decoding="async"
      referrerPolicy="no-referrer"
      className="max-h-[70vh] w-auto max-w-full object-contain"
      onError={onError}
      data-testid="viewer-image"
    />
  )
}

export default CustomerPhotoViewer
