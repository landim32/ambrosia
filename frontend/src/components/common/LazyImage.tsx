import { useTranslation } from 'react-i18next'
import { useEffect, useState } from 'react'
import { ImageOff } from 'lucide-react'

import { cn } from '@/lib/utils'

export interface LazyImageProps {
  /** URL da imagem como veio da API (`OrderItemInfo.imageUrl`), ou nada. */
  src: string | null
  /** Descrição do produto; o placeholder tem rótulo próprio. */
  alt: string
  className?: string
}

/**
 * Imagem do item do pedido (FR-023, T078).
 *
 * `loading="lazy"` + `decoding="async"`: a lista de itens não baixa tudo de uma
 * vez no celular da loja. `referrerPolicy="no-referrer"`: o servidor de imagem
 * do +Vendas não recebe a origem do Ambrosia — o domínio da vendedora não vaza.
 *
 * Sem URL, ou quando a URL quebrou (hotlink bloqueado ou link expirado — o Edge
 * Case da spec), aparece o placeholder NO MESMO ESPAÇO: um ícone sobre fundo
 * neutro, anunciado como imagem. Nada de imagem quebrada nem de layout que pula.
 *
 * O "quebrou" é por URL, não por componente: uma URL nova tenta de novo, para a
 * página 2 da lista não herdar o erro da página 1.
 */
export const LazyImage = ({ src, alt, className }: LazyImageProps) => {
  const { t } = useTranslation()
  const [failedSrc, setFailedSrc] = useState<string | null>(null)

  // URL diferente da que falhou volta a tentar (a instância é reutilizada pela
  // lista ao trocar de página).
  useEffect(() => {
    if (failedSrc !== null && failedSrc !== src) setFailedSrc(null)
  }, [src, failedSrc])

  const usableSrc =
    src !== null && src.trim() !== '' && src !== failedSrc ? src.trim() : null

  return (
    <div
      className={cn('aspect-square w-16 shrink-0 overflow-hidden rounded-md bg-muted', className)}
    >
      {usableSrc === null ? (
        <div
          role="img"
          aria-label={t('orders.noImage')}
          data-testid="image-placeholder"
          className="flex size-full items-center justify-center text-muted-foreground"
        >
          <ImageOff aria-hidden="true" className="size-6" />
        </div>
      ) : (
        <img
          src={usableSrc}
          alt={alt}
          loading="lazy"
          decoding="async"
          referrerPolicy="no-referrer"
          className="size-full object-cover"
          onError={() => {
            setFailedSrc(src)
          }}
        />
      )}
    </div>
  )
}

export default LazyImage
