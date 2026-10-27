import { useTranslation } from 'react-i18next'

import logoUrl from '@/assets/ambrosio-logo.webp'
import { cn } from '@/lib/utils'

export interface BrandLogoProps {
  className?: string
  /**
   * `true` quando o nome já é lido ao lado (ex.: título "Entrar no Ambrosio"):
   * a imagem vira decorativa e o leitor de tela não repete a marca.
   */
  decorative?: boolean
}

/**
 * Logomarca em texto do Ambrosio (wordmark branco com o "A" em azul/ciano),
 * extraída de `docs/marketing/logo-texto.png` com fundo transparente para
 * assentar sobre o tema escuro. A altura vem de `className` (ex.: `h-6`).
 */
export const BrandLogo = ({ className, decorative = false }: BrandLogoProps) => {
  const { t } = useTranslation()

  return (
    <img
      src={logoUrl}
      alt={decorative ? '' : t('app.logoAlt')}
      {...(decorative ? { 'aria-hidden': true } : {})}
      width={720}
      height={137}
      decoding="async"
      draggable={false}
      className={cn('h-6 w-auto select-none', className)}
    />
  )
}

export default BrandLogo
