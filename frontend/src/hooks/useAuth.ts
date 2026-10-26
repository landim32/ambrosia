import { useContext } from 'react'

import { AuthContext, type AuthContextType } from '@/Contexts/AuthContext'

/**
 * Acesso à sessão da vendedora. Lança se usada fora do `AuthProvider`
 * (skill `react-architecture`, regra do null-check).
 */
export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth deve ser usado dentro de AuthProvider')
  }

  return context
}

export default useAuth
