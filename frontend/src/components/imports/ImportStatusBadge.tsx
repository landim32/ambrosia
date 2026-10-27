import { useTranslation } from 'react-i18next'

import { Badge } from '@/components/ui/badge'
import { IMPORT_BLOCK_STATUS_KEYS, IMPORT_STATUS_KEYS } from '@/types/import'

import type { ImportRun, ImportRunBlock } from '@/types/import'

interface RunStatusBadgeProps {
  status: ImportRun['status']
}

interface BlockStatusBadgeProps {
  status: ImportRunBlock['status']
}

/**
 * Etiqueta de status da execução (feature 006, FR-050). `failed` e `partial` usam
 * `destructive` — o destaque que deixa a execução problemática visível no
 * histórico sem ler o texto; `queued` é `outline` ("Aguardando", FR-017). O texto
 * vem sempre de `import.status.*`: o valor cru do enum nunca vai para a tela.
 */
export const ImportStatusBadge = ({ status }: RunStatusBadgeProps) => {
  const { t } = useTranslation()

  const variant =
    status === 'failed' || status === 'partial' ? 'destructive' : status === 'queued' ? 'outline' : 'secondary'

  return (
    <Badge variant={variant} data-testid={`import-status-${status}`}>
      {t(IMPORT_STATUS_KEYS[status])}
    </Badge>
  )
}

/** Etiqueta de status de um bloco (FR-013): `failed` em destaque, o resto neutro. */
export const ImportBlockStatusBadge = ({ status }: BlockStatusBadgeProps) => {
  const { t } = useTranslation()

  return (
    <Badge
      variant={status === 'failed' ? 'destructive' : status === 'notExecuted' ? 'outline' : 'secondary'}
      data-testid={`import-block-status-${status}`}
    >
      {t(IMPORT_BLOCK_STATUS_KEYS[status])}
    </Badge>
  )
}

export default ImportStatusBadge
