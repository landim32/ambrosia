import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { useAuth } from '@/hooks/useAuth'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

const linkClass = ({ isActive }: { isActive: boolean }): string =>
  cn(
    'inline-flex min-h-11 items-center rounded-md px-3 text-sm font-medium whitespace-nowrap',
    'focus-visible:outline-none',
    isActive ? 'bg-accent text-accent-foreground' : 'text-muted-foreground hover:text-foreground',
  )

/**
 * Cabeçalho de todas as telas internas (FR-005/FR-006): identidade da vendedora
 * vinda da SESSÃO guardada (`session.user`), navegação Clientes/Pedidos e "Sair".
 * `GET /users/me` não é chamado aqui (contracts/api-consumption.md).
 */
export const AppHeader = () => {
  const { t } = useTranslation()
  const { session, logout } = useAuth()
  const user = session?.user

  return (
    <header className="border-b bg-card">
      <div className="mx-auto flex max-w-screen-md flex-wrap items-center gap-x-2 gap-y-1 px-4 py-2">
        <span className="min-w-0 grow basis-32">
          <span className="block truncate text-sm font-semibold text-foreground">
            {user?.name ?? t('login.title')}
          </span>
          <span className="block truncate text-xs text-muted-foreground">{user?.email ?? ''}</span>
        </span>

        <nav aria-label={t('common.customers')} className="flex items-center gap-1">
          <NavLink to="/clientes" className={linkClass}>
            {t('common.customers')}
          </NavLink>
          <NavLink to="/pedidos" className={linkClass}>
            {t('common.orders')}
          </NavLink>
          <NavLink to="/importacao" className={linkClass}>
            {t('import.nav')}
          </NavLink>
        </nav>

        <Button
          type="button"
          variant="outline"
          size="sm"
          className="min-h-11 min-w-11"
          aria-label={t('login.signOutLabel')}
          onClick={() => {
            logout()
          }}
        >
          {t('common.logout')}
        </Button>
      </div>
    </header>
  )
}

export default AppHeader
