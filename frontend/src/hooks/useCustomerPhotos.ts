import { useContext } from 'react'

import { CustomerPhotoContext, type CustomerPhotoContextType } from '@/Contexts/CustomerPhotoContext'

/**
 * Acesso ao álbum de fotos da cliente (feature 009). Lança se usado fora do
 * `CustomerPhotoProvider` (skill `react-architecture`, regra do null-check).
 */
export const useCustomerPhotos = (): CustomerPhotoContextType => {
  const context = useContext(CustomerPhotoContext)
  if (!context) {
    throw new Error('useCustomerPhotos deve ser usado dentro de CustomerPhotoProvider')
  }

  return context
}

export default useCustomerPhotos
