import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ChevronRight } from 'lucide-react'

import { CustomerAvatar } from '@/components/customers/CustomerAvatar'
import { maskedCpfShape } from '@/lib/cpf'

import type { CustomerInfo } from '@/types/customer'

export interface CustomerCardProps {
  customer: CustomerInfo
}

/**
 * Linha de cliente na listagem (FR-008/FR-010), no estilo das listas agrupadas
 * do iOS: avatar, nome, CPF mascarado e o chevron — a linha inteira é o link
 * para o perfil. O contêiner arredondado e os separadores ficam na lista
 * (`CustomersPage`).
 *
 * - O CPF passa por `maskedCpfShape`: só o formato da API aparece, e um valor
 *   inesperado nunca vira CPF completo em listagem (FR-022).
 * - Nome longo com `truncate` dentro de `min-w-0` (FR-028, Edge Case "nome muito
 *   longo"), e o rótulo acessível traz o nome inteiro para o leitor de tela.
 * - O `Link` é o único alvo de toque, com `min-h-11` (≥ 44 px, FR-028) e o foco
 *   visível preservado (FR-029).
 */
export const CustomerCard = ({ customer }: CustomerCardProps) => {
  const { t } = useTranslation()

  return (
    <Link
      to={`/clientes/${String(customer.id)}`}
      aria-label={t('customers.openProfile', { name: customer.name })}
      className="flex min-h-16 items-center gap-3 px-4 py-2.5 transition-colors hover:bg-secondary/60 focus-visible:outline-none focus-visible:ring-inset active:bg-secondary"
    >
      <CustomerAvatar name={customer.name} photoUrl={customer.photoUrl} />
      <span className="min-w-0 grow">
        <span className="block truncate text-[0.9375rem] font-medium text-foreground">
          {customer.name}
        </span>
        <span className="block font-mono text-xs text-muted-foreground tabular-nums">
          {maskedCpfShape(customer.cpfMasked)}
        </span>
      </span>
      <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-muted-foreground/60" />
    </Link>
  )
}

export default CustomerCard
