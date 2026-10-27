import { Navigate, Outlet, useLocation } from 'react-router-dom'

import { useAuth } from '@/hooks/useAuth'

/**
 * Guarda de rotas (FR-001/FR-007). Sem sessão, redireciona para `/login`
 * guardando a rota original (pathname + query) em `state.returnTo`, para que o
 * login devolva a vendedora ao endereço que ela tentou abrir (inclusive um
 * endereço compartilhado como `/pedidos?tipo=sale`).
 *
 * A sessão já chega validada do `AuthProvider`: `peekSession` descarta chave
 * corrompida ou vencida e o próprio provider navega com o aviso de expiração.
 */
export const RequireAuth = () => {
  const location = useLocation()
  const { isAuthenticated, expiryNotice } = useAuth()

  if (!isAuthenticated) {
    // A expiração chega por aqui também: sem o motivo na URL a vendedora
    // não veria "Sua sessão expirou" (FR-004), porque esta navegação
    // substitui a que o AuthProvider fez para /login?motivo=expirada.
    return (
      <Navigate
        to={expiryNotice ? '/login?motivo=expirada' : '/login'}
        replace
        state={{ returnTo: `${location.pathname}${location.search}` }}
      />
    )
  }

  return <Outlet />
}

export default RequireAuth
