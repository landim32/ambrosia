import { useEffect, useId, useRef, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ChevronRight, CircleUserRound, LogOut, X } from 'lucide-react'

import { useAuth } from '@/hooks/useAuth'
import { NAV_ITEMS, SETTINGS_ITEM, isSettingsPath } from '@/components/layout/navItems'
import { Button } from '@/components/ui/button'
import { initials } from '@/lib/initials'
import { cn } from '@/lib/utils'

const tabClass = (active: boolean): string =>
  cn(
    'flex min-h-11 flex-1 flex-col items-center justify-center gap-0.5 rounded-xl pt-1.5 pb-1 text-[0.6875rem] font-medium leading-none transition-colors',
    'focus-visible:outline-none',
    active ? 'text-brand-soft' : 'text-muted-foreground active:text-foreground',
  )

/**
 * Tab bar do rodapé no estilo iOS (só no mobile, `md:hidden`): material
 * translúcido, ícone + rótulo, item ativo no azul da marca e respiro para o
 * home indicator (`pb-safe`). Cada item tem ≥ 44 px de altura (FR-028).
 *
 * O último item, "Conta", abre uma folha (sheet) com a identidade da vendedora,
 * o link para "Configurações" e o "Sair" — no desktop isso fica no menu da
 * conta da barra superior (`AppHeader`). A folha fecha com Esc, com toque fora
 * e ao trocar de rota; "Conta" fica marcado enquanto a rota é de configuração.
 */
export const TabBar = () => {
  const { t } = useTranslation()
  const { session, logout } = useAuth()
  const location = useLocation()
  const [accountOpen, setAccountOpen] = useState(false)
  const sheetId = useId()
  const closeRef = useRef<HTMLButtonElement>(null)
  const user = session?.user
  const onSettings = isSettingsPath(location.pathname)
  const SettingsIcon = SETTINGS_ITEM.icon

  useEffect(() => {
    setAccountOpen(false)
  }, [location.pathname])

  useEffect(() => {
    if (!accountOpen) return undefined

    closeRef.current?.focus()

    const onKey = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') setAccountOpen(false)
    }
    window.addEventListener('keydown', onKey)

    return () => {
      window.removeEventListener('keydown', onKey)
    }
  }, [accountOpen])

  return (
    <>
      {accountOpen ? (
        <div className="fixed inset-0 z-50 md:hidden">
          <button
            type="button"
            tabIndex={-1}
            aria-label={t('nav.close')}
            className="absolute inset-0 bg-black/60 backdrop-blur-[2px]"
            onClick={() => {
              setAccountOpen(false)
            }}
          />
          <div
            id={sheetId}
            role="dialog"
            aria-modal="true"
            aria-label={t('nav.accountTitle')}
            className="pb-safe absolute inset-x-0 bottom-0 rounded-t-2xl border-t border-bar-border bg-popover"
          >
            <div className="px-safe mx-auto flex max-w-md flex-col gap-4 pt-2 pb-4">
              <div aria-hidden="true" className="mx-auto h-1.5 w-10 rounded-full bg-muted-foreground/30" />
              <div className="flex items-center justify-between gap-3">
                <h2 className="text-base font-semibold text-foreground">{t('nav.accountTitle')}</h2>
                {/* `<button>` nativo: o `Button` do shadcn não repassa `ref` no React 18. */}
                <button
                  ref={closeRef}
                  type="button"
                  className="inline-flex size-11 items-center justify-center rounded-full bg-secondary text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none"
                  aria-label={t('nav.close')}
                  onClick={() => {
                    setAccountOpen(false)
                  }}
                >
                  <X aria-hidden="true" className="size-5" />
                </button>
              </div>

              <div className="flex min-w-0 items-center gap-3 rounded-xl bg-secondary p-3">
                <span
                  aria-hidden="true"
                  className="flex size-12 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand to-brand-navy text-base font-semibold text-white"
                >
                  {initials(user?.name ?? '')}
                </span>
                <span className="min-w-0">
                  <span className="block truncate text-sm font-semibold text-foreground">
                    {user?.name ?? t('login.title')}
                  </span>
                  <span className="block truncate text-sm text-muted-foreground">{user?.email ?? ''}</span>
                </span>
              </div>

              <NavLink
                to={SETTINGS_ITEM.to}
                className="flex min-h-12 items-center gap-3 rounded-xl bg-secondary px-4 text-sm font-medium text-foreground transition-colors focus-visible:outline-none active:bg-accent"
              >
                <SettingsIcon aria-hidden="true" className="size-5 text-muted-foreground" />
                {t(SETTINGS_ITEM.labelKey)}
                <ChevronRight aria-hidden="true" className="ml-auto size-4 text-muted-foreground" />
              </NavLink>

              <Button
                type="button"
                variant="secondary"
                className="min-h-12 w-full rounded-xl text-destructive"
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
        </div>
      ) : null}

      <nav
        aria-label={t('nav.mainLabel')}
        className="bar-material pb-safe fixed inset-x-0 bottom-0 z-40 border-t border-bar-border md:hidden"
      >
        <div className="px-safe mx-auto flex h-(--tabbar-height) max-w-md items-stretch gap-1">
          {NAV_ITEMS.map(({ to, labelKey, icon: Icon }) => (
            <NavLink key={to} to={to} className={({ isActive }) => tabClass(isActive)}>
              {({ isActive }) => (
                <>
                  <Icon aria-hidden="true" className="size-6" strokeWidth={isActive ? 2.25 : 1.75} />
                  <span>{t(labelKey)}</span>
                </>
              )}
            </NavLink>
          ))}

          <button
            type="button"
            className={tabClass(accountOpen || onSettings)}
            aria-haspopup="dialog"
            aria-expanded={accountOpen}
            aria-controls={accountOpen ? sheetId : undefined}
            onClick={() => {
              setAccountOpen((open) => !open)
            }}
          >
            <CircleUserRound aria-hidden="true" className="size-6" strokeWidth={accountOpen || onSettings ? 2.25 : 1.75} />
            <span>{t('nav.account')}</span>
          </button>
        </div>
      </nav>
    </>
  )
}

export default TabBar
