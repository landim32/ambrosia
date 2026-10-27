import { Link } from 'react-router-dom'
import { ChevronLeft } from 'lucide-react'

export interface BackLinkProps {
  to: string
  label: string
}

/**
 * "Voltar" no estilo iOS (chevron + nome da tela de origem, no azul da marca)
 * para as telas de detalhe. Destino fixo — a lista da seção —, para o link
 * funcionar igual quando o endereço é aberto direto (PWA, link compartilhado).
 */
export const BackLink = ({ to, label }: BackLinkProps) => (
  <Link
    to={to}
    className="-ml-2 inline-flex min-h-11 items-center gap-0.5 self-start rounded-lg pr-3 pl-1 text-[0.9375rem] text-brand-soft transition-opacity hover:opacity-80 focus-visible:outline-none"
  >
    <ChevronLeft aria-hidden="true" className="size-6" strokeWidth={2.25} />
    {label}
  </Link>
)

export default BackLink
