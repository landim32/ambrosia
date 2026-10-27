import { useEffect, useState } from 'react'

import { imageService } from '@/Services/imageService'

/**
 * URL de leitura de uma imagem do storage (`imageId` do item do pedido).
 *
 * `undefined` enquanto carrega; `null` sem imagem ou quando a consulta falhou —
 * aí a tela mostra o placeholder, sem derrubar o pedido.
 */
export const useStoredImageUrl = (imageId: number | null): string | null | undefined => {
  const [state, setState] = useState<{ id: number | null; url: string | null | undefined }>({
    id: imageId,
    url: imageId === null ? null : undefined,
  })

  useEffect(() => {
    if (imageId === null) return

    let active = true
    imageService
      .getReadUrl(imageId)
      .then((url) => {
        if (active) setState({ id: imageId, url })
      })
      .catch(() => {
        if (active) setState({ id: imageId, url: null })
      })

    return () => {
      active = false
    }
  }, [imageId])

  if (imageId === null) return null
  return state.id === imageId ? state.url : undefined
}

export default useStoredImageUrl
