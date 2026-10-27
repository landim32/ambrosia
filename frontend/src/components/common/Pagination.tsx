import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'

export interface PaginationProps {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

/**
 * Paginação das três listas (FR-008/011/013): "Anterior"/"Próxima" desabilitados
 * nos limites, "Página X de Y" e o total de registros. Alvos de toque >= 44 px.
 * Toda string vem do recurso pt-BR (FR-024).
 */
export const Pagination = ({ page, pageSize, totalCount, onPageChange }: PaginationProps) => {
  const { t } = useTranslation()

  const pages = Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize)))
  const current = Math.min(Math.max(1, page), pages)
  const hasPrevious = current > 1
  const hasNext = current < pages

  const goTo = (target: number): void => {
    onPageChange(target)
  }

  return (
    <nav aria-label={t('common.paginationLabel')} className="flex flex-col items-center gap-3">
      <div className="flex w-full items-center justify-between gap-2 sm:w-auto">
        <Button
          type="button"
          variant="outline"
          className="min-h-11 min-w-11 flex-1 sm:flex-none"
          disabled={!hasPrevious}
          onClick={() => {
            goTo(current - 1)
          }}
          aria-label={t('common.previous')}
        >
          {t('common.previous')}
        </Button>

        <p className="whitespace-nowrap text-sm text-muted-foreground">
          {t('common.pageOf', { page: current, pages })}
        </p>

        <Button
          type="button"
          variant="outline"
          className="min-h-11 min-w-11 flex-1 sm:flex-none"
          disabled={!hasNext}
          onClick={() => {
            goTo(current + 1)
          }}
          aria-label={t('common.next')}
        >
          {t('common.next')}
        </Button>
      </div>

      <p aria-live="polite" className="text-sm text-muted-foreground">
        {t('common.records', { count: totalCount })}
      </p>
    </nav>
  )
}
