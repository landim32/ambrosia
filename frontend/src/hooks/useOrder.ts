import { useContext } from 'react'

import { OrderContext, type OrderContextType } from '@/Contexts/OrderContext'

/**
 * Acesso à consulta de pedidos. Lança se usada fora do `OrderProvider`
 * (skill `react-architecture`, regra do null-check).
 */
export const useOrder = (): OrderContextType => {
  const context = useContext(OrderContext)
  if (!context) {
    throw new Error('useOrder deve ser usado dentro de OrderProvider')
  }

  return context
}

export default useOrder
