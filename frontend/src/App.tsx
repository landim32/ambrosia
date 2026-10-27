import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'

import { AppShell } from '@/components/layout/AppShell'
import { LoadingList } from '@/components/common/LoadingList'
import { RequireAuth } from '@/components/layout/RequireAuth'

const LoginPage = lazy(() => import('@/pages/LoginPage'))
const RegisterPage = lazy(() => import('@/pages/RegisterPage'))
const CustomersPage = lazy(() => import('@/pages/CustomersPage'))
const CustomerProfilePage = lazy(() => import('@/pages/CustomerProfilePage'))
const OrdersPage = lazy(() => import('@/pages/OrdersPage'))
const OrderDetailPage = lazy(() => import('@/pages/OrderDetailPage'))
const NotFoundPage = lazy(() => import('@/pages/NotFoundPage'))

/** `VITE_SITE_BASENAME` vazio significa "na raiz da origem" (sem basename). */
export const siteBasename = (): string | undefined => {
  const value = import.meta.env.VITE_SITE_BASENAME

  return value === undefined || value === '' ? undefined : value
}

/**
 * Rotas do SPA (contracts/ui-routes.md). Uma chunk por página via `React.lazy`
 * (research §19). `BrowserRouter` vive em `main.tsx` porque o `AuthContext`
 * precisa de `useNavigate` (T039). Toda rota interna passa por `RequireAuth`
 * (FR-001) dentro de `AppShell` (cabeçalho com nome/e-mail e navegação,
 * FR-005/FR-006); `/login` e `/cadastro` são as públicas (003: criar conta tem
 * rota própria, com o mesmo `state.returnTo`). Nenhuma rota usa os prefixos
 * `/api`, `/auth`, `/mcp` ou `/.well-known` (gateway).
 */
export const App = () => (
  <Suspense fallback={<LoadingList count={3} />}>
    <Routes>
      <Route path="/" element={<Navigate to="/clientes" replace />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/cadastro" element={<RegisterPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppShell />}>
          <Route path="/clientes" element={<CustomersPage />} />
          <Route path="/clientes/:id" element={<CustomerProfilePage />} />
          <Route path="/pedidos" element={<OrdersPage />} />
          <Route path="/pedidos/:id" element={<OrderDetailPage />} />
          {/* FR-001: até a tela de "página não encontrada" é interna. */}
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  </Suspense>
)

export default App
