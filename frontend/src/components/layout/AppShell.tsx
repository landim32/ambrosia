import { Outlet } from 'react-router-dom'

import { AppHeader } from '@/components/layout/AppHeader'

/**
 * Casca das telas internas: cabeçalho fixo + área de conteúdo mobile-first.
 * `min-w-0` no container evita a rolagem horizontal com textos longos (FR-028).
 */
export const AppShell = () => (
  <div className="min-h-dvh bg-background">
    <AppHeader />
    <main className="mx-auto w-full max-w-screen-md min-w-0 px-4 py-4">
      <Outlet />
    </main>
  </div>
)

export default AppShell
