import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ImagePlus } from 'lucide-react'

import { cn } from '@/lib/utils'

import { MAX_PHOTO_BYTES, isAcceptedPhotoType } from '@/types/customerPhoto'

export interface CustomerPhotoDropzoneProps {
  /** Recebe SÓ os arquivos aceitos; a recusa local de tipo/tamanho já avisou. */
  onFiles: (files: File[]) => void
}

const MAX_MEGABYTES = Math.round(MAX_PHOTO_BYTES / (1024 * 1024))

/** `true` quando o arraste traz arquivos (texto e link solto são ignorados). */
const dragsFiles = (event: DragEvent): boolean => {
  const types = event.dataTransfer?.types

  return types !== undefined && Array.from(types).includes('Files')
}

/**
 * Área de inclusão da aba Fotos (contracts/ui.md §3, FR-020).
 *
 * Uma linha discreta — borda tracejada e texto `muted-foreground` — que não
 * compete com as fotos (US2-6). No celular o mesmo toque abre o seletor de fotos
 * do aparelho (`input type=file` escondido), porque arrastar não existe lá
 * (US2-4). O texto troca por "Toque para incluir uma foto" em
 * `@media (pointer: coarse)` (variante `pointer-coarse` do Tailwind 4).
 *
 * Os eventos de arraste são os NATIVOS `dragenter`/`dragover`/`drop` (research
 * R9: sem `react-dropzone`), e escutam a JANELA: o contrato destaca a área
 * quando o arraste entra em qualquer ponto da aba, não só em cima da linha. A
 * área só existe montada na aba Fotos, então fora dela nada escuta.
 *
 * A recusa local (tipo fora da lista, tamanho acima de 10 MB) acontece AQUI, com
 * o nome do arquivo no aviso, antes de qualquer requisição — o servidor confere
 * de novo do lado dele, com as regras da feature 004 (FR-003 revisado).
 */
export const CustomerPhotoDropzone = ({ onFiles }: CustomerPhotoDropzoneProps) => {
  const { t } = useTranslation()

  const inputRef = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)

  /**
   * Contador de entradas: `dragleave` dispara em CADA elemento filho que o cursor
   * cruza, e sem ele a área piscaria o destaque para fora no meio do arraste.
   */
  const depth = useRef(0)

  /** Arquivos aceitos vão para a fila; o resto é recusado com o nome no aviso. */
  const accept = useCallback(
    (list: FileList | null): void => {
      if (list === null || list.length === 0) return

      const accepted: File[] = []

      Array.from(list).forEach((file) => {
        // Tipo DECLARADO pelo navegador, como na 004: ninguém abre o arquivo aqui.
        if (!isAcceptedPhotoType(file.type)) {
          toast.warning(t('customerPhotos.rejectedType', { name: file.name }))

          return
        }

        if (file.size > MAX_PHOTO_BYTES) {
          toast.warning(
            t('customerPhotos.rejectedSize', { name: file.name, max: String(MAX_MEGABYTES) }),
          )

          return
        }

        accepted.push(file)
      })

      if (accepted.length === 0) return

      onFiles(accepted)
    },
    [onFiles, t],
  )

  useEffect(() => {
    const onDragEnter = (event: Event): void => {
      const drag = event as DragEvent
      if (!dragsFiles(drag)) return

      drag.preventDefault()
      depth.current += 1
      setDragging(true)
    }

    // Sem `preventDefault` no `dragover` o navegador NUNCA dispara o `drop`.
    const onDragOver = (event: Event): void => {
      const drag = event as DragEvent
      if (!dragsFiles(drag)) return

      drag.preventDefault()

      if (drag.dataTransfer) drag.dataTransfer.dropEffect = 'copy'
    }

    const onDragLeave = (event: Event): void => {
      const drag = event as DragEvent
      if (!dragsFiles(drag)) return

      depth.current = Math.max(0, depth.current - 1)
      if (depth.current === 0) setDragging(false)
    }

    const onDrop = (event: Event): void => {
      const drag = event as DragEvent
      if (!dragsFiles(drag)) return

      drag.preventDefault()
      depth.current = 0
      setDragging(false)

      accept(drag.dataTransfer?.files ?? null)
    }

    window.addEventListener('dragenter', onDragEnter)
    window.addEventListener('dragover', onDragOver)
    window.addEventListener('dragleave', onDragLeave)
    window.addEventListener('drop', onDrop)

    return () => {
      window.removeEventListener('dragenter', onDragEnter)
      window.removeEventListener('dragover', onDragOver)
      window.removeEventListener('dragleave', onDragLeave)
      window.removeEventListener('drop', onDrop)
    }
  }, [accept])

  return (
    <div
      className={cn(
        'rounded-lg border border-dashed border-border transition-colors',
        dragging && 'border-brand bg-brand/5',
      )}
      data-testid="photo-dropzone"
      data-dragging={dragging ? 'true' : 'false'}
    >
      {/* Alvo de toque com os 44 px da FR-028; o `input` escondido faz o resto. */}
      <button
        type="button"
        className="flex min-h-11 w-full items-center justify-center gap-2 px-3 py-2 text-sm text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
        onClick={() => {
          inputRef.current?.click()
        }}
        data-testid="photo-drop-button"
      >
        <ImagePlus aria-hidden="true" className="size-4 shrink-0" />
        <span className="pointer-coarse:hidden">{t('customerPhotos.dropHere')}</span>
        <span className="hidden pointer-coarse:inline">{t('customerPhotos.dropTap')}</span>
      </button>

      <input
        ref={inputRef}
        type="file"
        multiple
        accept="image/jpeg,image/png,image/webp"
        className="hidden"
        onChange={(event) => {
          accept(event.target.files)

          // Zera a seleção: escolher o MESMO arquivo duas vezes precisa disparar
          // de novo (o `change` só fire quando o valor muda).
          event.target.value = ''
        }}
        data-testid="photo-input"
        tabIndex={-1}
        aria-hidden="true"
      />
    </div>
  )
}

export default CustomerPhotoDropzone
