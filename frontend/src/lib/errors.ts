import { toast } from 'sonner'

import { ApiError, NetworkError } from '@/Services/apiClient'

/**
 * Traduz o erro do `apiClient` em mensagem para a vendedora (FR-026).
 * `NetworkError` → "Não foi possível conectar"; `ApiError` → a mensagem da API;
 * qualquer outro → mensagem genérica (nenhum stack trace vai para a tela).
 */
export const toUserMessage = (err: unknown, t: (key: string) => string): string => {
  if (err instanceof NetworkError) return t('errors.network')
  if (err instanceof ApiError) return err.mensagem !== '' ? err.mensagem : t('errors.unexpected')
  if (err instanceof Error) return err.message !== '' ? err.message : t('errors.unexpected')
  return t('errors.unexpected')
}

/**
 * Toast de erro com a ação "Tentar de novo" (skill `react-alert`, FR-026).
 * `onRetry` recarrega a última consulta da tela.
 */
export const notifyError = (
  err: unknown,
  t: (key: string) => string,
  onRetry?: () => void,
): void => {
  const message = toUserMessage(err, t)

  toast.error(message, {
    ...(onRetry
      ? {
          action: {
            label: t('common.retry'),
            onClick: () => {
              onRetry()
            },
          },
        }
      : {}),
  })
}
