import { CloudDownload, ReceiptText, Settings, Users } from 'lucide-react'

import type { LucideIcon } from 'lucide-react'

/**
 * Destino da configuração do +Vendas (feature 008, research R10): a MESMA string
 * da rota em `App.tsx`, do link "Ir para a configuração" (FR-009) e do link que o
 * plugin devolve na recusa (`{Mcp:FrontendBaseUrl}/configuracoes/mais-vendas`,
 * research R9) — se os três divergissem, a vendedora cairia em 404 justamente
 * quando o sistema está mandando ela para lá.
 */
export const MAIS_VENDAS_SETTINGS_PATH = '/configuracoes/mais-vendas'

/** Uma seção principal do app: mesma lista no topo (desktop) e na tab bar (mobile). */
export interface NavItem {
  to: string
  /** Chave i18n do rótulo. */
  labelKey: 'nav.customers' | 'nav.orders' | 'nav.import' | 'nav.settings'
  icon: LucideIcon
}

export const NAV_ITEMS: readonly NavItem[] = [
  { to: '/clientes', labelKey: 'nav.customers', icon: Users },
  { to: '/pedidos', labelKey: 'nav.orders', icon: ReceiptText },
  { to: '/importacao', labelKey: 'nav.import', icon: CloudDownload },
  // Feature 008: "Configurações" no menu (contracts/ui.md), destino do aviso da importação.
  { to: MAIS_VENDAS_SETTINGS_PATH, labelKey: 'nav.settings', icon: Settings },
]
