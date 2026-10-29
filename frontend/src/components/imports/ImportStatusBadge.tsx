import { useTranslation } from 'react-i18next'

import { Badge } from '@/components/ui/badge'
import {
  IMPORT_BLOCK_STATUS_KEYS,
  IMPORT_RUN_KIND_KEYS,
  IMPORT_STARTED_VIA_KEYS,
  IMPORT_STATUS_KEYS,
} from '@/types/import'

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

/**
 * Etiqueta de origem/o quê de uma execução (feature 008, research R8, T027).
 *
 * Só desenha o que NÃO é o padrão: `startedVia: 'screen'` e `kind: 'period'` são o
 * que a vendedora acabou de fazer na tela, e repetir isso em toda linha do
 * histórico seria ruído. O que merece rótulo é a execução que APARECEU sozinha no
 * polling — iniciada pelo plugin do ChatGPT (SC-002) — e o "Atualizar base", cujo
 * período vem calculado do servidor e se distingue da importação por datas
 * (FR-013). Sem o rótulo, um progresso que ela não iniciou parece um bug.
 */
export const ImportRunOriginBadges = ({ run }: { run: ImportRun }) => {
  const { t } = useTranslation()

  if (run.startedVia === 'screen' && run.kind === 'period') return null

  return (
    <>
      {run.startedVia === 'mcp' ? (
        <Badge variant="outline" data-testid="import-via-plugin">
          {t(IMPORT_STARTED_VIA_KEYS.mcp)}
        </Badge>
      ) : null}

      {run.kind === 'updateBase' ? (
        <Badge variant="secondary" data-testid="import-kind-update-base">
          {t(IMPORT_RUN_KIND_KEYS.updateBase)}
        </Badge>
      ) : null}
    </>
  )
}

export default ImportStatusBadge
