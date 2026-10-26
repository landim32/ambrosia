import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { CustomerAvatar } from '@/components/customers/CustomerAvatar'
import { Card } from '@/components/ui/card'
import { maskedCpfShape } from '@/lib/cpf'

import type { CustomerInfo } from '@/types/customer'

export interface CustomerCardProps {
  customer: CustomerInfo
}

/**
 * Card de cliente na listagem (FR-008/FR-010): avatar, nome e CPF mascarado, e o
 * card inteiro é o link para o perfil.
 *
 * - O CPF passa por `maskedCpfShape`: só o formato da API aparece, e um valor
 *   inesperado nunca vira CPF completo em listagem (FR-022).
 * - Nome longo com `truncate` dentro de `min-w-0` (FR-028, Edge Case "nome muito
 *   longo"), e o rótulo acessível traz o nome inteiro para o leitor de tela.
 * - O `Link` é o único alvo de toque do card, com `min-h-11` (≥ 44 px, FR-028)
 *   e o foco visível do shadcn preservado (FR-029).
 */
export const CustomerCard = ({ customer }: CustomerCardProps) => {
  const { t } = useTranslation()

  return (
    <Card className="border bg-card transition-colors hover:bg-accent/40">
      <Link
        to={`/clientes/${String(customer.id)}`}
        aria-label={t('customers.openProfile', { name: customer.name })}
        className="flex min-h-11 items-center gap-3 rounded-lg px-4 py-3 focus-visible:outline-none"
      >
        <CustomerAvatar name={customer.name} photoUrl={customer.photoUrl} />
        <span className="min-w-0 grow">
          <span className="block truncate text-sm font-medium text-foreground">
            {customer.name}
          </span>
          <span className="block font-mono text-xs text-muted-foreground tabular-nums">
            {maskedCpfShape(customer.cpfMasked)}
          </span>
        </span>
      </Link>
    </Card>
  )
}

export default CustomerCard
