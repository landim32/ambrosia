import { Link, NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { LogOut } from 'lucide-react'

import { useAuth } from '@/hooks/useAuth'
import { BrandLogo } from '@/components/layout/BrandLogo'
import { NAV_ITEMS } from '@/components/layout/navItems'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

const linkClass = ({ isActive }: { isActive: boolean }): string =>
  cn(
    'inline-flex min-h-11 items-center gap-2 rounded-full px-4 text-sm font-medium whitespace-nowrap transition-colors',
    'focus-visible:outline-none',
    isActive
      ? 'bg-accent text-accent-foreground'
      : 'text-muted-foreground hover:bg-secondary hover:text-foreground',
  )

/**
 * Barra superior de todas as telas internas, no material translúcido do iOS
 * (fundo escuro a 72% + blur), presa ao topo e descontando a safe area do
 * notch/Dynamic Island.
 *
 * - Mobile: só a logomarca — a navegação mora na tab bar do rodapé (`TabBar`).
 * - Desktop (md+): logomarca, seções em pílulas e a identidade da vendedora
 *   vinda da SESSÃO guardada (`session.user`, FR-005/FR-006) com "Sair".
 *   `GET /users/me` não é chamado aqui (contracts/api-consumption.md).
 */
export const AppHeader = () => {
  const { t } = useTranslation()
  const { session, logout } = useAuth()
  const user = session?.user

  return (
    <header className="bar-material pt-safe sticky top-0 z-40 border-b border-bar-border">
      <div className="px-safe mx-auto flex h-12 max-w-screen-lg items-center gap-4 md:h-16">
        <Link
          to="/clientes"
          className="-mx-2 inline-flex min-h-11 items-center rounded-lg px-2 focus-visible:outline-none"
        >
          <BrandLogo className="h-5 md:h-6" />
        </Link>

        <nav aria-label={t('nav.mainLabel')} className="hidden items-center gap-1 md:flex">
          {NAV_ITEMS.map(({ to, labelKey, icon: Icon }) => (
            <NavLink key={to} to={to} className={linkClass}>
              <Icon aria-hidden="true" className="size-4" />
              {t(labelKey)}
            </NavLink>
          ))}
        </nav>

        <div className="ml-auto hidden min-w-0 items-center gap-3 md:flex">
          <span className="min-w-0 text-right">
            <span className="block max-w-48 truncate text-sm font-medium text-foreground">
              {user?.name ?? t('login.title')}
            </span>
            <span className="block max-w-48 truncate text-xs text-muted-foreground">
              {user?.email ?? ''}
            </span>
          </span>

          <Button
            type="button"
            variant="ghost"
            className="min-h-11 min-w-11 rounded-full text-muted-foreground hover:text-foreground"
            aria-label={t('login.signOutLabel')}
            onClick={() => {
              logout()
            }}
          >
            <LogOut aria-hidden="true" />
            {t('common.logout')}
          </Button>
        </div>
      </div>
    </header>
  )
}

export default AppHeader
