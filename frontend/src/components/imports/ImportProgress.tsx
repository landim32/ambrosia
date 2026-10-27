import { useTranslation } from 'react-i18next'

import { ImportBlockStatusBadge, ImportStatusBadge } from '@/components/imports/ImportStatusBadge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { COUNT_KEYS, countLabelKey, formatImportDate, ordersWrittenOf } from '@/lib/importForm'

import type { ImportRunCounts, ImportRunDetail } from '@/types/import'

interface Props {
  run: ImportRunDetail
  /** `true` enquanto o POST/cancelamento está em voo. */
  busy?: boolean
  onCancel: () => void
  onRefresh: () => void
}

/**
 * Progresso da execução em curso (feature 006, FR-014/FR-015/FR-017/FR-032, US4-2).
 *
 * Mostra o bloco em andamento, quantos terminaram e as contagens parciais — os
 * três números que a vendedora precisa enquanto a coleta roda, com o texto
 * "Aguardando" quando `queued` (a execução está na fila do semáforo, FR-017).
 *
 * Em modo `dryRun` o rótulo "Simulação" aparece no cabeçalho e as contagens usam
 * os textos "seriam…" (FR-032): a vendedora não pode ler um progresso de
 * simulação como se já tivesse gravado.
 *
 * O botão "Cancelar" fecha o ciclo da FR-019: `queued` cancela de imediato,
 * `running` encerra ao fim do bloco em curso — e a tela diz isso, porque "cancelar"
 * que não cancela na hora parece bug.
 */
export const ImportProgress = ({ run, busy = false, onCancel, onRefresh }: Props) => {
  const { t } = useTranslation()

  const isDryRun = run.mode === 'dryRun'
  const finished = run.blocksTotal > 0 && run.blocksFinished >= run.blocksTotal

  const counts = COUNT_KEYS.map((key): [keyof ImportRunCounts, number] => [key, run.counts[key]])

  return (
    <Card className="gap-4" data-testid="import-progress">
      <CardHeader className="gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="grow text-lg font-semibold text-foreground">
            {isDryRun ? t('import.simulationTag') : t('import.title')}
          </h2>
          <ImportStatusBadge status={run.status} />
        </div>

        <p className="text-sm text-muted-foreground">
          {t('import.periodRange', {
            start: formatImportDate(run.startDate),
            end: formatImportDate(run.endDate),
          })}
          {' · '}
          {t('import.modeLabel')}: {t(isDryRun ? 'import.mode.dryRun' : 'import.mode.apply')}
        </p>

        {run.status === 'queued' ? (
          <p className="text-sm text-muted-foreground" data-testid="import-queued">
            {t('import.queued')}
          </p>
        ) : null}

        <p className="text-sm text-foreground" data-testid="import-block-progress">
          {run.currentBlockNo === null
            ? t('import.blocksFinished', { finished: run.blocksFinished, total: run.blocksTotal })
            : t('import.blockProgress', { current: run.currentBlockNo, total: run.blocksTotal })}
        </p>

        <p className="text-xs text-muted-foreground">{t('import.progressHelp')}</p>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {counts.map(([key, value]) => (
            <li key={key} className="rounded-md border p-2">
              <p className="text-xs text-muted-foreground">{t(countLabelKey(key, isDryRun))}</p>
              <p className="text-lg font-semibold tabular-nums text-foreground" data-testid={`count-${key}`}>
                {value}
              </p>
            </li>
          ))}
        </ul>

        <ul className="flex flex-col gap-1" aria-label={t('import.blocksTitle')}>
          {run.blocks.map((block) => (
            <li
              key={block.blockNo}
              className="flex flex-wrap items-center gap-2 rounded-md border px-3 py-2 text-sm"
              data-testid={`progress-block-${String(block.blockNo)}`}
            >
              <span className="font-medium text-foreground">
                {t('import.blockRow', { number: block.blockNo })}
              </span>
              <span className="text-xs text-muted-foreground">
                {formatImportDate(block.startDate)} – {formatImportDate(block.endDate)}
              </span>
              <ImportBlockStatusBadge status={block.status} />
              <span className="ml-auto text-xs tabular-nums text-muted-foreground">
                {t('import.ordersWritten', { count: ordersWrittenOf(block.counts) })}
              </span>
            </li>
          ))}
        </ul>

        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            className="min-h-11"
            disabled={busy}
            onClick={onRefresh}
          >
            {t('import.refresh')}
          </Button>

          {run.status === 'running' && !run.cancelRequested ? (
            <Button
              type="button"
              variant="destructive"
              className="min-h-11"
              disabled={busy}
              onClick={onCancel}
              data-testid="import-cancel"
            >
              {t('import.cancelRun')}
            </Button>
          ) : null}

          {run.cancelRequested && !finished ? (
            <p className="text-sm text-muted-foreground">{t('import.cancelToast')}</p>
          ) : null}
        </div>
      </CardContent>
    </Card>
  )
}

export default ImportProgress
