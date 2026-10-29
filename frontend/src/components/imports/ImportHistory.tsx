import { useTranslation } from 'react-i18next'

import { Pagination } from '@/components/common/Pagination'
import { ImportRunOriginBadges, ImportStatusBadge } from '@/components/imports/ImportStatusBadge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { formatImportDate, formatImportInstant, ordersWrittenOf } from '@/lib/importForm'

import type { PagedResult } from '@/types/common'
import type { ImportRun } from '@/types/import'

interface Props {
  history: PagedResult<ImportRun> | null
  /** Execução cujo relatório está aberto (destacada na lista). */
  selectedRunId: number | null
  onOpen: (runId: number) => void
  onPageChange: (page: number) => void
}

/**
 * Histórico das importações da vendedora (feature 006, FR-045/FR-050), mais
 * recentes primeiro. Cada linha traz período, modo, status — com destaque para
 * falha e parcial — e quantos pedidos foram vistos, e abre o relatório completo.
 * O histórico nunca é apagado (Assumptions da spec); a paginação é a da API.
 */
export const ImportHistory = ({ history, selectedRunId, onOpen, onPageChange }: Props) => {
  const { t } = useTranslation()

  return (
    <Card className="gap-4" data-testid="import-history">
      <CardHeader>
        <h2 className="text-lg font-semibold text-foreground">{t('import.historyTitle')}</h2>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        {history === null || history.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('import.historyEmpty')}</p>
        ) : (
          <>
            <ul className="flex flex-col divide-y rounded-md border">
              {history.items.map((run) => (
                <li key={run.id}>
                  <Button
                    type="button"
                    variant={run.id === selectedRunId ? 'secondary' : 'ghost'}
                    className="h-auto min-h-11 w-full flex-col items-stretch gap-1 rounded-none px-3 py-2 text-left"
                    onClick={() => {
                      onOpen(run.id)
                    }}
                    data-testid={`history-run-${String(run.id)}`}
                  >
                    <span className="flex flex-wrap items-center gap-2">
                      <span className="text-sm font-medium text-foreground">
                        {t('import.periodRange', {
                          start: formatImportDate(run.startDate),
                          end: formatImportDate(run.endDate),
                        })}
                      </span>
                      <ImportStatusBadge status={run.status} />
                      <span className="text-xs text-muted-foreground">
                        {t(run.mode === 'dryRun' ? 'import.mode.dryRun' : 'import.mode.apply')}
                      </span>
                      {/*
                        * Feature 008 (research R8): "Pelo plugin" e "Atualizar base"
                        * só aparecem quando diferem do padrão — a linha de uma
                        * importação normal fica como estava na feature 006.
                       */}
                      <ImportRunOriginBadges run={run} />
                    </span>
                    <span className="flex flex-wrap gap-2 text-xs font-normal text-muted-foreground">
                      <span>{formatImportInstant(run.requestedAt)}</span>
                      <span>{t('import.ordersWritten', { count: ordersWrittenOf(run.counts) })}</span>
                    </span>
                  </Button>
                </li>
              ))}
            </ul>

            <Pagination
              page={history.page}
              pageSize={history.pageSize}
              totalCount={history.totalCount}
              onPageChange={onPageChange}
            />
          </>
        )}
      </CardContent>
    </Card>
  )
}

export default ImportHistory
