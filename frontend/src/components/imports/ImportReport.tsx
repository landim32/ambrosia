import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'

import { ImportBlockStatusBadge, ImportStatusBadge } from '@/components/imports/ImportStatusBadge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import {
  COUNT_KEYS,
  countLabelKey,
  formatImportDate,
  formatImportInstant,
  ordersWrittenOf,
  repeatBlockHref,
} from '@/lib/importForm'
import { orderKindLabel } from '@/lib/orderKind'
import { IMPORT_ACTION_KEYS } from '@/types/import'

import type { ImportRunChange, ImportRunDetail } from '@/types/import'

interface Props {
  run: ImportRunDetail
}

/** Rótulo i18n da origem (Cota/Omni); ausente vira traço. */
const originKey = (origin: ImportRunChange['origin']): string | null =>
  origin === null ? null : origin === 'cota' ? 'orders.originCota' : 'orders.originOmni'

/**
 * Relatório de uma execução (feature 006, FR-047 a FR-051, US3/US4/US7).
 *
 * Quatro partes, na ordem em que a vendedora decide o que fazer:
 * 1. **Resumo**: status (com destaque em falha/parcial, FR-050), modo, período,
 *    duração, mensagem do motivo e as nove contagens — no condicional quando é
 *    simulação (FR-032), para ninguém ler "inseridos" onde nada foi gravado.
 * 2. **Aviso de queda** (FR-051): esta execução viu menos pedidos que a anterior
 *    do mesmo período — a origem pode ter respondido incompleto.
 * 3. **Blocos**: cada um com status, período e pedidos gravados; bloco falho tem
 *    "Repetir este bloco", que leva ao formulário com o período do bloco (FR-026).
 *    Login e senha **não** vão no link: a vendedora digita de novo (FR-007).
 * 4. **Agrupamento e rastro**: canal × origem × tipo × ação (FR-049) e a lista de
 *    pedidos, com link para `/pedidos/{id}` só quando o pedido existe — nunca em
 *    simulação, em erro ou em não encontrado sem pedido (contrato imports-api.md).
 *
 * Nenhuma linha mostra CPF ou nome de cliente (FR-052): o rastro da API só traz a
 * chave natural do pedido.
 */
export const ImportReport = ({ run }: Props) => {
  const { t } = useTranslation()

  const isDryRun = run.mode === 'dryRun'

  return (
    <Card className="gap-4" data-testid="import-report">
      <CardHeader className="gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="grow text-lg font-semibold text-foreground">{t('import.resultLabel')}</h2>
          <ImportStatusBadge status={run.status} />
        </div>

        {isDryRun ? (
          <p className="text-sm font-medium text-foreground" data-testid="import-simulation-tag">
            {t('import.simulationTag')}
          </p>
        ) : null}

        <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
          <dt className="text-muted-foreground">{t('import.period')}</dt>
          <dd className="text-foreground">
            {t('import.periodRange', {
              start: formatImportDate(run.startDate),
              end: formatImportDate(run.endDate),
            })}
          </dd>
          <dt className="text-muted-foreground">{t('import.modeLabel')}</dt>
          <dd className="text-foreground">{t(isDryRun ? 'import.mode.dryRun' : 'import.mode.apply')}</dd>
          <dt className="text-muted-foreground">{t('import.startedAt')}</dt>
          <dd className="text-foreground">{formatImportInstant(run.startedAt ?? run.requestedAt)}</dd>
          {run.durationSeconds !== null ? (
            <>
              <dt className="text-muted-foreground">{t('import.duration')}</dt>
              <dd className="text-foreground">{t('import.durationSeconds', { seconds: run.durationSeconds })}</dd>
            </>
          ) : null}
        </dl>

        {run.errorMessage !== null ? (
          <p
            role="alert"
            data-testid="import-error-message"
            className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive"
          >
            {run.errorMessage}
          </p>
        ) : null}

        {run.countDropWarning ? (
          <p
            role="alert"
            data-testid="import-count-drop"
            className="rounded-md border border-destructive/40 bg-card px-3 py-2 text-sm text-destructive"
          >
            {t('import.countDropWarning', { previous: run.previousRunOrdersCount ?? 0 })}
          </p>
        ) : null}
      </CardHeader>

      <CardContent className="flex flex-col gap-6">
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {COUNT_KEYS.map((key) => (
            <li key={key} className="rounded-md border p-2">
              <p className="text-xs text-muted-foreground">{t(countLabelKey(key, isDryRun))}</p>
              <p className="text-lg font-semibold tabular-nums text-foreground" data-testid={`report-count-${key}`}>
                {run.counts[key]}
              </p>
            </li>
          ))}
        </ul>

        <section className="flex flex-col gap-2" aria-labelledby="import-blocks-title">
          <h3 id="import-blocks-title" className="text-base font-semibold text-foreground">
            {t('import.blocksTitle')}
          </h3>
          <ul className="flex flex-col gap-2">
            {run.blocks.map((block) => (
              <li
                key={block.blockNo}
                className="flex flex-col gap-1 rounded-md border px-3 py-2 text-sm"
                data-testid={`report-block-${String(block.blockNo)}`}
              >
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium text-foreground">{t('import.blockRow', { number: block.blockNo })}</span>
                  <span className="text-xs text-muted-foreground">
                    {formatImportDate(block.startDate)} – {formatImportDate(block.endDate)}
                  </span>
                  <ImportBlockStatusBadge status={block.status} />
                  <span className="ml-auto text-xs tabular-nums text-muted-foreground">
                    {t('import.ordersWritten', { count: ordersWrittenOf(block.counts) })}
                  </span>
                </div>

                {block.errorMessage !== null ? (
                  <p className="text-xs text-destructive">{block.errorMessage}</p>
                ) : null}

                {block.status === 'failed' ? (
                  <Button asChild variant="outline" className="min-h-11 self-start">
                    <Link
                      to={repeatBlockHref({ start: block.startDate, end: block.endDate })}
                      data-testid={`repeat-block-${String(block.blockNo)}`}
                    >
                      {t('import.repeatBlock')}
                    </Link>
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        </section>

        {run.breakdown.length > 0 ? (
          <section className="flex flex-col gap-2" aria-labelledby="import-breakdown-title">
            <h3 id="import-breakdown-title" className="text-base font-semibold text-foreground">
              {t('import.breakdownTitle')}
            </h3>
            <ul className="flex flex-col gap-1 text-sm" data-testid="import-breakdown">
              {run.breakdown.map((row) => {
                const origin = originKey(row.origin)

                return (
                  <li
                    key={`${row.channel ?? ''}|${row.origin ?? ''}|${row.kind}|${row.action}`}
                    className="text-foreground"
                  >
                    {t('import.breakdownRow', {
                      channel: row.channel ?? t('import.originAny'),
                      origin: origin === null ? t('import.originAny') : t(origin),
                      kind: t(orderKindLabel(row.kind)),
                      action: t(IMPORT_ACTION_KEYS[row.action]),
                      count: row.count,
                    })}
                  </li>
                )
              })}
            </ul>
          </section>
        ) : null}

        {run.changes.length > 0 ? (
          <section className="flex flex-col gap-2" aria-labelledby="import-changes-title">
            <h3 id="import-changes-title" className="text-base font-semibold text-foreground">
              {t('import.changesTitle')}
            </h3>
            <ul className="flex flex-col divide-y rounded-md border text-sm" data-testid="import-changes">
              {run.changes.map((change) => (
                <li
                  key={`${change.orderNumber}|${change.orderDate}|${change.kind}|${change.action}`}
                  className="flex flex-col gap-0.5 px-3 py-2"
                >
                  <div className="flex flex-wrap items-center gap-2">
                    {change.orderId !== null && !isDryRun ? (
                      <Link
                        to={`/pedidos/${String(change.orderId)}`}
                        className="font-medium text-foreground underline underline-offset-2"
                        aria-label={t('import.openedOrder', { number: change.orderNumber })}
                      >
                        {change.orderNumber}
                      </Link>
                    ) : (
                      <span className="font-medium text-foreground">{change.orderNumber}</span>
                    )}
                    <span className="text-xs text-muted-foreground">{formatImportDate(change.orderDate)}</span>
                    <span className="text-xs text-muted-foreground">{t(orderKindLabel(change.kind))}</span>
                    <span className="ml-auto text-xs font-medium text-foreground">
                      {t(IMPORT_ACTION_KEYS[change.action])}
                    </span>
                  </div>

                  {change.changedFields !== null && change.changedFields.length > 0 ? (
                    <p className="text-xs text-muted-foreground">
                      {t('import.changedFields', { fields: change.changedFields.join(', ') })}
                    </p>
                  ) : null}

                  {change.errorMessage !== null ? (
                    <p className="text-xs text-destructive">{change.errorMessage}</p>
                  ) : null}
                </li>
              ))}
            </ul>
          </section>
        ) : null}
      </CardContent>
    </Card>
  )
}

export default ImportReport
