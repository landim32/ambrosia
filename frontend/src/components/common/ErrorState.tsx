import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'

export interface ErrorStateProps {
  message: string
  /** Recarrega a última consulta (FR-025/026). */
  onRetry: () => void
}

/**
 * Erro de consulta com ação de repetição. A mensagem vem de `toUserMessage`
 * (`lib/errors.ts`): a mensagem devolvida pela API, ou "Não foi possível conectar".
 */
export const ErrorState = ({ message, onRetry }: ErrorStateProps) => {
  const { t } = useTranslation()

  return (
    <div
      role="alert"
      className="flex flex-col items-center justify-center gap-3 rounded-lg border border-destructive/40 bg-card px-4 py-10 text-center"
      data-testid="error-state"
    >
      <p className="text-sm font-medium text-destructive">{message}</p>
      <Button type="button" variant="outline" className="min-h-11" onClick={onRetry}>
        {t('common.retry')}
      </Button>
    </div>
  )
}
