import { CloudDownload, ReceiptText, Users } from 'lucide-react'

import type { LucideIcon } from 'lucide-react'

/** Uma seção principal do app: mesma lista no topo (desktop) e na tab bar (mobile). */
export interface NavItem {
  to: string
  /** Chave i18n do rótulo. */
  labelKey: 'nav.customers' | 'nav.orders' | 'nav.import'
  icon: LucideIcon
}

export const NAV_ITEMS: readonly NavItem[] = [
  { to: '/clientes', labelKey: 'nav.customers', icon: Users },
  { to: '/pedidos', labelKey: 'nav.orders', icon: ReceiptText },
  { to: '/importacao', labelKey: 'nav.import', icon: CloudDownload },
]
