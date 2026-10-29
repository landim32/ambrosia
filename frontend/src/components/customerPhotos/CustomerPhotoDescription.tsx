import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'

import { MAX_PHOTO_DESCRIPTION_LENGTH } from '@/types/customerPhoto'

export interface CustomerPhotoDescriptionProps {
  /** Descrição salva; `null` = sem descrição (FR-007). */
  description: string | null
  /**
   * Grava no contexto (que fala com a API). Devolve `true` quando salvou — só
   * aí o editor fecha; recusou, a vendedora continua com o texto na tela.
   */
  onSave: (description: string | null) => Promise<boolean>
}

/**
 * Descrição da foto no visualizador (contracts/ui.md §4, FR-007, US4-3 a US4-5).
 *
 * Modo leitura: o texto com "Editar" ao lado, ou "Adicionar descrição" quando
 * está vazia. Modo escrita: `textarea` com contador `n/500`, Salvar e Cancelar,
 * e `Ctrl/Cmd+Enter` salvando — a vendedora que digita com as duas mãos não
 * larga o teclado.
 *
 * Acima de 500 caracteres o Salvar fica desabilitado COM aviso: a API responde
 * 400 com `erros[0].campo = "description"` (US4-5), e travar antes é mais rápido
 * que deixar a vendedora descobrir pelo erro.
 */
export const CustomerPhotoDescription = ({ description, onSave }: CustomerPhotoDescriptionProps) => {
  const { t } = useTranslation()

  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(description ?? '')
  const [saving, setSaving] = useState(false)
  const field = useRef<HTMLTextAreaElement>(null)

  const tooLong = draft.length > MAX_PHOTO_DESCRIPTION_LENGTH

  /** Abriu, foco no campo e seleção no fim: editar é o único motivo de abrir. */
  useEffect(() => {
    if (!editing) return

    field.current?.focus()
  }, [editing])

  const cancel = useCallback((): void => {
    setDraft(description ?? '')
    setEditing(false)
  }, [description])

  const save = useCallback(async (): Promise<void> => {
    if (tooLong || saving) return

    const text = draft.trim()

    // Vazio ou só espaços é "sem descrição" (FR-007): a API grava `null`, e a
    // tela manda `null` em vez de esperar que o servidor interprete o vazio.
    setSaving(true)

    const saved = await onSave(text === '' ? null : text)

    setSaving(false)

    if (!saved) return

    setEditing(false)
  }, [draft, onSave, saving, tooLong])

  if (!editing) {
    const hasText = description !== null && description.trim() !== ''

    return (
      <div className="flex min-w-0 flex-wrap items-baseline gap-2" data-testid="description-view">
        {hasText ? (
          <p className="min-w-0 grow text-sm break-words text-foreground">{description}</p>
        ) : null}
        <Button
          type="button"
          size="sm"
          variant="ghost"
          className="min-h-11 shrink-0 px-2 text-sm text-brand-soft"
          onClick={() => {
            setDraft(description ?? '')
            setEditing(true)
          }}
        >
          {hasText ? t('customerPhotos.descriptionEdit') : t('customerPhotos.descriptionAdd')}
        </Button>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-1.5" data-testid="description-editor">
      <label htmlFor="photo-description" className="text-xs text-muted-foreground">
        {t('customerPhotos.descriptionLabel')}
      </label>

      <textarea
        id="photo-description"
        ref={field}
        rows={3}
        className="min-h-11 w-full resize-y rounded-lg border border-border bg-background p-2 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
        value={draft}
        onChange={(event) => {
          setDraft(event.target.value)
        }}
        onKeyDown={(event) => {
          // Ctrl/Cmd+Enter salva (contracts/ui.md §4).
          if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
            event.preventDefault()

            void save()
          }
        }}
        data-testid="description-textarea"
      />

      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-xs text-muted-foreground tabular-nums">
          {t('customerPhotos.descriptionCount', {
            current: String(draft.length),
            max: String(MAX_PHOTO_DESCRIPTION_LENGTH),
          })}
        </span>

        {tooLong ? (
          <p className="text-xs text-destructive" data-testid="description-warning">
            {t('customerPhotos.descriptionTooLong', {
              max: String(MAX_PHOTO_DESCRIPTION_LENGTH),
            })}
          </p>
        ) : null}

        <div className="flex gap-2">
          <Button
            type="button"
            size="sm"
            variant="ghost"
            className="min-h-11"
            disabled={saving}
            onClick={cancel}
          >
            {t('customerPhotos.descriptionCancel')}
          </Button>
          <Button
            type="button"
            size="sm"
            className="min-h-11"
            disabled={tooLong || saving}
            onClick={() => {
              void save()
            }}
            data-testid="description-save"
          >
            {t('customerPhotos.descriptionSave')}
          </Button>
        </div>
      </div>
    </div>
  )
}

export default CustomerPhotoDescription
