import { useEffect, useId, useRef, useState } from 'react'
import { Link, NavLink, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ChevronDown, LogOut } from 'lucide-react'

import { useAuth } from '@/hooks/useAuth'
import { BrandLogo } from '@/components/layout/BrandLogo'
import { NAV_ITEMS, SETTINGS_ITEM, isSettingsPath } from '@/components/layout/navItems'
import { initials } from '@/lib/initials'
import { cn } from '@/lib/utils'

const linkClass = ({ isActive }: { isActive: boolean }): string =>
  cn(
    'inline-flex min-h-11 items-center gap-2 rounded-full px-4 text-sm font-medium whitespace-nowrap transition-colors',
    'focus-visible:outline-none',
    isActive
      ? 'bg-accent text-accent-foreground'
      : 'text-muted-foreground hover:bg-secondary hover:text-foreground',
  )

const menuItemClass =
  'flex min-h-11 w-full items-center gap-3 rounded-lg px-3 text-sm font-medium transition-colors hover:bg-secondary focus-visible:bg-secondary focus-visible:outline-none'

/**
 * Barra superior de todas as telas internas, no material translúcido do iOS
 * (fundo escuro a 72% + blur), presa ao topo e descontando a safe area do
 * notch/Dynamic Island.
 *
 * - Mobile: só a logomarca — a navegação mora na tab bar do rodapé (`TabBar`).
 * - Desktop (md+): logomarca, seções em pílulas e o menu "Conta" com a
 *   identidade da vendedora vinda da SESSÃO guardada (`session.user`,
 *   FR-005/FR-006), "Configurações" e "Sair". O painel fecha com Esc, com
 *   clique fora e ao trocar de rota. `GET /users/me` não é chamado aqui
 *   (contracts/api-consumption.md).
 */
export const AppHeader = () => {
  const { t } = useTranslation()
  const { session, logout } = useAuth()
  const location = useLocation()
  const [accountOpen, setAccountOpen] = useState(false)
  const menuId = useId()
  const menuRef = useRef<HTMLDivElement>(null)
  const user = session?.user
  const onSettings = isSettingsPath(location.pathname)
  const SettingsIcon = SETTINGS_ITEM.icon

  useEffect(() => {
    setAccountOpen(false)
  }, [location.pathname])

  useEffect(() => {
    if (!accountOpen) return undefined

    const onKey = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') setAccountOpen(false)
    }
    const onPointer = (event: MouseEvent): void => {
      if (!menuRef.current?.contains(event.target as Node)) setAccountOpen(false)
    }
    window.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onPointer)

    return () => {
      window.removeEventListener('keydown', onKey)
      document.removeEventListener('mousedown', onPointer)
    }
  }, [accountOpen])

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

        <div ref={menuRef} className="relative ml-auto hidden min-w-0 md:block">
          <button
            type="button"
            className={cn(
              'flex min-h-11 min-w-0 items-center gap-3 rounded-full py-1 pr-3 pl-1 transition-colors focus-visible:outline-none',
              accountOpen || onSettings ? 'bg-accent' : 'hover:bg-secondary',
            )}
            aria-label={t('nav.account')}
            aria-haspopup="true"
            aria-expanded={accountOpen}
            aria-controls={accountOpen ? menuId : undefined}
            onClick={() => {
              setAccountOpen((open) => !open)
            }}
          >
            <span
              aria-hidden="true"
              className="flex size-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand to-brand-navy text-sm font-semibold text-white"
            >
              {initials(user?.name ?? '')}
            </span>
            <span className="min-w-0 text-left">
              <span className="block max-w-48 truncate text-sm font-medium text-foreground">
                {user?.name ?? t('login.title')}
              </span>
              <span className="block max-w-48 truncate text-xs text-muted-foreground">
                {user?.email ?? ''}
              </span>
            </span>
            <ChevronDown
              aria-hidden="true"
              className={cn('size-4 shrink-0 text-muted-foreground transition-transform', accountOpen && 'rotate-180')}
            />
          </button>

          {accountOpen ? (
            <div
              id={menuId}
              aria-label={t('nav.accountTitle')}
              className="absolute right-0 mt-2 w-60 rounded-xl border border-bar-border bg-popover p-1.5 shadow-lg"
            >
              <NavLink to={SETTINGS_ITEM.to} className={cn(menuItemClass, 'text-foreground')}>
                <SettingsIcon aria-hidden="true" className="size-4 text-muted-foreground" />
                {t(SETTINGS_ITEM.labelKey)}
              </NavLink>
              <button
                type="button"
                className={cn(menuItemClass, 'text-destructive')}
                aria-label={t('login.signOutLabel')}
                onClick={() => {
                  logout()
                }}
              >
                <LogOut aria-hidden="true" className="size-4" />
                {t('common.logout')}
              </button>
            </div>
          ) : null}
        </div>
      </div>
    </header>
  )
}

export default AppHeader
