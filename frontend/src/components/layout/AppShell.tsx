import { Outlet } from 'react-router-dom'

import { AppHeader } from '@/components/layout/AppHeader'
import { TabBar } from '@/components/layout/TabBar'

/**
 * Casca das telas internas, no padrão de app do iPhone:
 *
 * - barra superior translúcida presa ao topo (`AppHeader`);
 * - conteúdo com respiro para a tab bar e para a safe area inferior no mobile
 *   (`pb-tabbar`), e as laterais respeitando o notch em paisagem (`px-safe`);
 * - tab bar no rodapé só no mobile (`TabBar`); no desktop a navegação fica no topo.
 *
 * `min-w-0` no container evita a rolagem horizontal com textos longos (FR-028).
 */
export const AppShell = () => (
  <div className="min-h-dvh bg-background">
    <AppHeader />
    <main className="px-safe pb-tabbar mx-auto w-full max-w-screen-lg min-w-0 pt-4 md:pt-8 md:pb-12">
      <Outlet />
    </main>
    <TabBar />
  </div>
)

export default AppShell
