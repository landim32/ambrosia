import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'

import { AppShell } from '@/components/layout/AppShell'
import { LoadingList } from '@/components/common/LoadingList'
import { MAIS_VENDAS_SETTINGS_PATH } from '@/components/layout/navItems'
import { RequireAuth } from '@/components/layout/RequireAuth'

const LoginPage = lazy(() => import('@/pages/LoginPage'))
const RegisterPage = lazy(() => import('@/pages/RegisterPage'))
const CustomersPage = lazy(() => import('@/pages/CustomersPage'))
const CustomerProfilePage = lazy(() => import('@/pages/CustomerProfilePage'))
const OrdersPage = lazy(() => import('@/pages/OrdersPage'))
const OrderDetailPage = lazy(() => import('@/pages/OrderDetailPage'))
const ImportPage = lazy(() => import('@/pages/ImportPage'))
const MaisVendasSettingsPage = lazy(() => import('@/pages/MaisVendasSettingsPage'))
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
          {/* Feature 006: importação do +Vendas. Protegida como as demais (FR-001). */}
          <Route path="/importacao" element={<ImportPage />} />
          {/*
            * Feature 008 (contracts/ui.md, research R10): a configuração do +Vendas.
            * O caminho é o MESMO da rota, do item do menu e do link que a tela de
            * importação oferece sem credenciais (FR-009) — todos lidos de
            * `MAIS_VENDAS_SETTINGS_PATH`, porque o plugin (research R9) devolve à
            * vendedora `{Mcp:FrontendBaseUrl}/configuracoes/mais-vendas`: se a rota
            * divergir do link, ela cai em "página não encontrada" justamente quando
            * o sistema está mandando ela para lá.
            */}
          <Route path={MAIS_VENDAS_SETTINGS_PATH} element={<MaisVendasSettingsPage />} />
          {/* FR-001: até a tela de "página não encontrada" é interna. */}
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  </Suspense>
)

export default App
