import { useTranslation } from 'react-i18next'

import { formatCurrency } from '@/lib/currency'
import { formatDateOnly, isValidDateOnly } from '@/lib/dates'
import { cn } from '@/lib/utils'

import type { CustomerSummaryInfo } from '@/types/customer'

export interface CustomerSummaryProps {
  summary: CustomerSummaryInfo
}

/** Nome do teste de cada campo, estável para os testes selecionarem sem ambiguidade. */
type Field = 'purchases' | 'netTotal' | 'lastPurchase' | 'days'

/**
 * Uma "estatística" do perfil: número grande em cima, rótulo discreto embaixo —
 * como os contadores de um perfil de rede social. O rótulo sempre aparece
 * (FR-029); o `dt` vem depois no visual (`order`) mas antes no DOM, para o
 * leitor de tela anunciar "rótulo: valor".
 */
const Stat = ({
  field,
  label,
  value,
  emphasis = false,
}: {
  field: Field
  label: string
  value: string
  emphasis?: boolean
}) => (
  <div className="flex min-w-0 flex-col items-center justify-center gap-1 px-2 py-3 text-center">
    <dt className="order-2 text-[0.6875rem] leading-tight text-muted-foreground">{label}</dt>
    <dd
      data-testid={`summary-${field}`}
      className={cn(
        'order-1 max-w-full truncate font-semibold tracking-tight text-foreground tabular-nums',
        emphasis ? 'text-lg sm:text-xl' : 'text-base sm:text-lg',
      )}
    >
      {value}
    </dd>
  </div>
)

/**
 * Resumo de compras do cliente (FR-010) como faixa de estatísticas do perfil:
 * quantidade, total líquido, última compra e dias desde a última compra.
 * Duas colunas no celular estreito, quatro a partir de `sm`.
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
    <dl
      className="grid grid-cols-2 divide-border/70 overflow-hidden rounded-2xl border border-border/70 bg-card sm:grid-cols-4 sm:divide-x max-sm:[&>*:nth-child(-n+2)]:border-b max-sm:[&>*:nth-child(odd)]:border-r"
      aria-label={t('customers.summaryAriaLabel')}
      data-testid="customer-summary"
    >
      <Stat
        field="purchases"
        label={t('customers.purchases')}
        value={String(summary.purchases)}
        emphasis
      />
      <Stat
        field="netTotal"
        label={t('customers.netTotal')}
        value={formatCurrency(summary.netTotal)}
        emphasis
      />
      <Stat field="lastPurchase" label={t('customers.lastPurchase')} value={lastPurchaseLabel} />
      <Stat
        field="days"
        label={t('customers.daysSinceLastPurchase')}
        value={summary.daysSinceLastPurchase === null ? dash : String(summary.daysSinceLastPurchase)}
      />
    </dl>
  )
}

export default CustomerSummary
