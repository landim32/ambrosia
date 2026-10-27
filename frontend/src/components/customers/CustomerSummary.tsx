import { useTranslation } from 'react-i18next'

import { Separator } from '@/components/ui/separator'
import { formatCurrency } from '@/lib/currency'
import { formatDateOnly, isValidDateOnly } from '@/lib/dates'

import type { CustomerSummaryInfo } from '@/types/customer'

export interface CustomerSummaryProps {
  summary: CustomerSummaryInfo
}

/** Nome do teste de cada campo, estável para os testes selecionarem sem ambiguidade. */
type Field = 'purchases' | 'netTotal' | 'lastPurchase' | 'days'

/** Uma linha rótulo/valor do resumo. O rótulo sempre aparece (FR-029). */
const SummaryRow = ({ field, label, value }: { field: Field; label: string; value: string }) => (
  <div className="flex items-baseline justify-between gap-3">
    <dt className="text-sm text-muted-foreground">{label}</dt>
    <dd
      data-testid={`summary-${field}`}
      className="text-sm font-medium text-foreground tabular-nums"
    >
      {value}
    </dd>
  </div>
)

/**
 * Resumo de compras do cliente (FR-010): quantidade, total líquido, última
 * compra e dias desde a última compra.
 *
 * `purchases: 0` com `lastPurchase: null` é o cliente que nunca comprou: mostra
 * "0", `R$ 0,00` e "Sem compras" (quickstart C2.7), nunca uma data vazia. A data
 * chega como dia-calendário `AAAA-MM-DD` e sai `dd/mm/aaaa` pelo `formatDateOnly`
 * — o mesmo dia em qualquer fuso (FR-020).
 */
export const CustomerSummary = ({ summary }: CustomerSummaryProps) => {
  const { t } = useTranslation()
  const dash = t('common.notAvailable')

  const hasLastPurchase = isValidDateOnly(summary.lastPurchase) && summary.purchases > 0
  const lastPurchaseLabel = hasLastPurchase
    ? (formatDateOnly(summary.lastPurchase) || dash)
    : t('customers.noPurchases')

  return (
    <dl className="grid gap-2 text-left" aria-label={t('customers.summaryAriaLabel')} data-testid="customer-summary">
      <SummaryRow field="purchases" label={t('customers.purchases')} value={String(summary.purchases)} />
      <SummaryRow field="netTotal" label={t('customers.netTotal')} value={formatCurrency(summary.netTotal)} />
      <Separator />
      <SummaryRow field="lastPurchase" label={t('customers.lastPurchase')} value={lastPurchaseLabel} />
      <SummaryRow
        field="days"
        label={t('customers.daysSinceLastPurchase')}
        value={summary.daysSinceLastPurchase === null ? dash : String(summary.daysSinceLastPurchase)}
      />
    </dl>
  )
}

export default CustomerSummary
