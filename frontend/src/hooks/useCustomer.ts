import { useContext } from 'react'

import { CustomerContext, type CustomerContextType } from '@/Contexts/CustomerContext'

/**
 * Acesso à consulta de clientes. Lança se usada fora do `AuthProvider`
 * (skill `react-architecture`, regra do null-check).
 */
export const useCustomer = (): CustomerContextType => {
  const context = useContext(CustomerContext)
  if (!context) {
    throw new Error('useCustomer deve ser usado dentro de CustomerProvider')
  }

  return context
}

export default useCustomer
