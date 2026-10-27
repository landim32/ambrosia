import { useTranslation } from 'react-i18next'

import { Skeleton } from '@/components/ui/skeleton'

export interface LoadingListProps {
  /** Quantidade de cartões placeholder. */
  count?: number
  /** Rótulo anunciado a leitores de tela; padrão `common.loading`. */
  label?: string
}

/**
 * Carregando de lista (FR-025): `count` cards com `Skeleton`, sinalizados com
 * `role="status"` + `aria-busy` para o leitor de tela anunciar a espera.
 */
export const LoadingList = ({ count = 3, label }: LoadingListProps) => {
  const { t } = useTranslation()
  const text = label ?? t('common.loading')

  return (
    <div
      role="status"
      aria-busy="true"
      aria-live="polite"
      aria-label={text}
      className="flex flex-col gap-3"
      data-testid="loading-list"
    >
      {Array.from({ length: count }, (_unused, index) => (
        <div key={index} className="rounded-lg border bg-card p-4">
          <div className="flex items-center gap-3">
            <Skeleton className="size-10 shrink-0 rounded-full" />
            <div className="flex-1 space-y-2">
              <Skeleton className="h-4 w-2/5" />
              <Skeleton className="h-3 w-1/4" />
            </div>
          </div>
          <Skeleton className="mt-4 h-4 w-3/5" />
        </div>
      ))}
    </div>
  )
}
