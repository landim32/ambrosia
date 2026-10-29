import { useCallback, useEffect, useRef, useState } from 'react'

import { imageService } from '@/Services/imageService'

export interface RenewedUrl {
  /** `src` corrente do `<img>`: a URL da listagem, ou a renovada depois da falha. */
  src: string
  /** Ligado no `onError` da imagem: renova UMA vez por URL (research R10). */
  onError: () => void
}

/**
 * URL de uma foto do álbum com renovação pelo caminho que já existe (feature 009,
 * research R10): o `GET /images/{imageId}` da feature 004, que cacheia por
 * `imageId` até perto de `urlExpiresAt` e compartilha as prometidas em voo. Não
 * existe rota avulsa de foto, e esta tela não inventa uma.
 *
 * Começa com a URL que veio da listagem. Quando o `<img>` falha (link vencido —
 * edge case "link temporário só renova por rota autenticada"), pede uma URL nova
 * uma vez só. Insistir seria loop: a `imageService` devolve a URL do cache, o
 * `src` não muda e o navegador nem tenta de novo.
 */
export const useRenewedUrl = (imageId: number, url: string): RenewedUrl => {
  const [src, setSrc] = useState<string>(url)
  const attempted = useRef<string | null>(null)

  // Outro arquivo (foto navegada, página relida): volta a ter direito a tentativa.
  useEffect(() => {
    setSrc(url)
    attempted.current = null
  }, [url])

  const onError = useCallback((): void => {
    if (attempted.current === url) return

    attempted.current = url

    imageService
      .getReadUrl(imageId)
      .then((fresh) => {
        if (fresh !== null && fresh !== '' && fresh !== url) setSrc(fresh)
      })
      .catch(() => {
        // Sem URL nova fica o que está: uma célula quebrada não derruba a grade.
      })
  }, [imageId, url])

  return { src, onError }
}

export default useRenewedUrl
