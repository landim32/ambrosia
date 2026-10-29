import { useCallback, useEffect, useRef, useState } from 'react'
import ReactCrop from 'react-easy-crop'
import { useTranslation } from 'react-i18next'

import { Modal } from '@/components/common/Modal'
import { Button } from '@/components/ui/button'
import { imageService } from '@/Services/imageService'

import type { RefObject } from 'react'
import type { Area, MediaSize } from 'react-easy-crop'
import type { CustomerPhotoInfo, CustomerProfilePhotoInfo } from '@/types/customerPhoto'
import { CropToFileError, cropToFile } from '@/lib/cropToFile'
import type { CropToFileReason } from '@/lib/cropToFile'
import { toUserMessage } from '@/lib/errors'

export interface ProfilePhotoCropperProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Foto do álbum sendo enquadrada — SEMPRE o original (contracts/ui.md §5). */
  photo: CustomerPhotoInfo | null
  /** `true` quando esta foto já é o perfil: oferece "Remover foto de perfil". */
  isCurrentProfile: boolean
  /**
   * `PUT .../profile-photo` com o recorte já enviado por `POST /images`. Devolve
   * o perfil salvo, ou `null` quando o servidor recusou (a mensagem dele foi
   * avisada) — nesse caso o editor fica aberto com o motivo (contracts/ui.md §5).
   */
  onSave: (imageId: number) => Promise<CustomerProfilePhotoInfo | null>
  /** `DELETE .../profile-photo`. */
  onRemoveProfile: () => Promise<boolean>
}

/** Lado mínimo, em pixels do arquivo, que o quadrado pode cobrir (US3-10). */
const MIN_SOURCE_SIDE_PX = 64

/** Zoom máximo pedido pelo contrato; pode cair conforme o tamanho do arquivo. */
const MAX_ZOOM = 4

const CROP_ERROR_KEYS: Record<CropToFileReason, string> = {
  'no-canvas': 'customerPhotos.cropErrorNoCanvas',
  'no-toblob': 'customerPhotos.cropErrorNoToBlob',
  'not-loaded': 'customerPhotos.cropErrorNotLoaded',
  'empty': 'customerPhotos.cropErrorEmpty',
  'security': 'customerPhotos.cropErrorSecurity',
  'bad-area': 'customerPhotos.cropErrorBadArea',
}

/**
 * Editor de recorte da foto de perfil (contracts/ui.md §5, FR-012, research R4).
 *
 * O quadrado é `react-easy-crop` com `aspect={1}` e `cropShape="round"` — a
 * prévia já é o círculo do avatar — e o ARQUIVO quadrado nasce AQUI, no
 * navegador: `cropToFile` desenha a área escolhida num `<canvas>` de 512 × 512 e
 * `toBlob` produz o WebP que sobe. Nenhum enquadramento é guardado no servidor
 * (R5): reabrir o editor recomeça centrado sobre o original da foto.
 *
 * Salvar são DOIS passos, e nenhum estado de perfil muda antes da resposta do
 * `PUT`: falhou em qualquer um, o editor continua aberto com o motivo e o botão
 * "Salvar" disponível de novo (contracts/ui.md §5).
 *
 * `crossOrigin="anonymous"` na imagem do cropper: sem CORS no storage o canvas
 * sai contaminado e `toBlob` recusa — por isso o erro tem frase própria em vez
 * de um botão que não faz nada.
 */
export const ProfilePhotoCropper = ({
  open,
  onOpenChange,
  photo,
  isCurrentProfile,
  onSave,
  onRemoveProfile,
}: ProfilePhotoCropperProps) => {
  const { t } = useTranslation()

  const [crop, setCrop] = useState({ x: 50, y: 50 })
  const [zoom, setZoom] = useState(1)
  const [maxZoom, setMaxZoom] = useState(MAX_ZOOM)
  const [area, setArea] = useState<Area | null>(null)
  const [saving, setSaving] = useState(false)
  const [removing, setRemoving] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const imageElement = useRef<RefObject<HTMLImageElement> | null>(null)

  /**
   * Reabrir o editor recomeça CENTRADO sobre a foto: nenhum enquadramento é
   * relido do servidor (research R4/R5) e a coordenada nem sequer é guardada.
   *
   * O reset é por TRANSIÇÃO (`aberto` + `foto`), não em todo mount, porque o
   * efeito do filho — que entrega a primeira área recortada — roda ANTES do
   * efeito do pai: resetando no mount, a área recém-chegada seria apagada.
   */
  const opened = useRef<string>(`${open ? 'o' : 'c'}:${String(photo?.photoId ?? 0)}`)

  useEffect(() => {
    const key = `${open ? 'o' : 'c'}:${String(photo?.photoId ?? 0)}`

    if (key === opened.current) return

    opened.current = key

    if (!open) return

    // `area` e `maxZoom` ficam de fora: quem os entrega é o filho, cujo efeito
    // rodou agora mesmo — resetá-los aqui apagaria o recorte recém-chegado.
    setCrop({ x: 50, y: 50 })
    setZoom(1)
    setMessage(null)
    setSaving(false)
    setRemoving(false)
  }, [open, photo?.photoId])

  const handleMediaLoaded = useCallback((media: MediaSize): void => {
    const shortSide = Math.min(media.naturalWidth, media.naturalHeight)
    if (shortSide <= 0) return

    // Zoom máximo limitado para o quadrado nunca cobrir menos que 64 px do
    // arquivo (US3-10): com `contain` + `restrictPosition`, o lado coberto é
    // `ladoCurto / zoom`, então `zoom ≤ ladoCurto / 64`.
    setMaxZoom(Math.max(1, Math.min(MAX_ZOOM, Math.floor(shortSide / MIN_SOURCE_SIDE_PX))))
  }, [])

  const handleCropComplete = useCallback((_croppedArea: Area, croppedAreaPixels: Area): void => {
    setArea(croppedAreaPixels)
  }, [])

  const save = useCallback(async (): Promise<void> => {
    const element = imageElement.current?.current
    if (element === undefined || element === null) {
      setMessage(t('customerPhotos.cropErrorNotLoaded'))

      return
    }

    if (area === null) {
      setMessage(t('customerPhotos.cropErrorBadArea'))

      return
    }

    setSaving(true)
    setMessage(null)

    try {
      const file = await cropToFile(element, area)
      const image = await imageService.upload(file, 'foto-perfil.webp')

      const saved = await onSave(image.imageId)

      // `onSave` já avisou o motivo e mantém o editor aberto (contracts/ui.md §5).
      if (saved === null) return

      onOpenChange(false)
    } catch (err) {
      setMessage(
        err instanceof CropToFileError
          ? t(CROP_ERROR_KEYS[err.reason])
          : toUserMessage(err, t),
      )
    } finally {
      setSaving(false)
    }
  }, [area, onOpenChange, onSave, t])

  const removeProfile = useCallback(async (): Promise<void> => {
    setRemoving(true)
    setMessage(null)

    const removed = await onRemoveProfile()

    setRemoving(false)

    if (removed) onOpenChange(false)
  }, [onOpenChange, onRemoveProfile])

  const closeLabel = t('customerPhotos.close')

  return (
    <Modal
      open={open && photo !== null}
      onOpenChange={onOpenChange}
      title={t('customerPhotos.cropperTitle')}
      closeLabel={closeLabel}
      className="max-w-2xl"
      footer={
        <>
          {isCurrentProfile ? (
            <Button
              type="button"
              variant="ghost"
              className="min-h-11 text-destructive"
              disabled={saving || removing}
              onClick={() => {
                void removeProfile()
              }}
              data-testid="crop-remove-profile"
            >
              {removing
                ? t('customerPhotos.removingProfile')
                : t('customerPhotos.removeProfile')}
            </Button>
          ) : null}

          <Button
            type="button"
            variant="ghost"
            className="min-h-11"
            disabled={saving}
            onClick={() => {
              onOpenChange(false)
            }}
          >
            {t('customerPhotos.cropCancel')}
          </Button>
          <Button
            type="button"
            className="min-h-11"
            disabled={saving}
            onClick={() => {
              void save()
            }}
            data-testid="crop-save"
          >
            {saving ? t('customerPhotos.cropSaving') : t('customerPhotos.cropSave')}
          </Button>
        </>
      }
    >
      {photo === null ? null : (
        <div className="flex flex-col gap-3">
          {/* Altura fixa: o cropper precisa de caixa com lado conhecido, e o
              quadrado inicial é o maior possível centrado (US3-1). */}
          <div className="relative h-72 w-full overflow-hidden rounded-xl bg-muted sm:h-96">
            <ReactCrop
              image={photo.url}
              crop={crop}
              onCropChange={setCrop}
              zoom={zoom}
              onZoomChange={setZoom}
              rotation={0}
              aspect={1}
              cropShape="round"
              showGrid={false}
              objectFit="contain"
              minZoom={1}
              maxZoom={maxZoom}
              restrictPosition
              onMediaLoaded={handleMediaLoaded}
              onCropComplete={handleCropComplete}
              setImageRef={(ref) => {
                imageElement.current = ref
              }}
              mediaProps={{
                crossOrigin: 'anonymous',
                referrerPolicy: 'no-referrer',
                alt: '',
              }}
            />
          </div>

          <div className="flex flex-col gap-1">
            <label htmlFor="crop-zoom" className="text-xs text-muted-foreground">
              {t('customerPhotos.zoom')}
            </label>
            <input
              id="crop-zoom"
              type="range"
              min={1}
              max={maxZoom}
              step={0.01}
              value={zoom}
              className="size-11 w-full accent-brand"
              onChange={(event) => {
                setZoom(Number(event.target.value))
              }}
              data-testid="crop-zoom"
            />
          </div>

          {message !== null ? (
            <p className="text-sm text-destructive" role="alert" data-testid="crop-message">
              {message}
            </p>
          ) : null}

          <p className="text-xs text-muted-foreground">
            {t('customerPhotos.cropperHint', {
              size: '512',
            })}
          </p>
        </div>
      )}
    </Modal>
  )
}

export default ProfilePhotoCropper
