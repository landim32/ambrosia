import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'

export interface NotFoundProps {
  title: string
  description?: string
  /** Rótulo do link de volta; padrão "Clientes". */
  linkLabel?: string
  /** Destino do link. Padrão `/clientes`. */
  linkTo?: string
}

/**
 * Recurso ou rota inexistente (FR-027). A MESMA tela é usada para id inexistente
 * e para recurso de outra vendedora, sem que nada as distinga.
 *
 * O título é `h1`: esta tela É a página quando o perfil, o pedido ou a rota não
 * existem — e cada página tem de ter um único `h1` (FR-029, T085). Um `h2` aqui
 * deixaria `Clientes não encontrado`/`Pedido não encontrado`/`Página não
 * encontrada` sem cabeçalho de nível 1 para o leitor de tela navegar.
 */
export const NotFound = ({ title, description, linkLabel, linkTo = '/clientes' }: NotFoundProps) => {
  const { t } = useTranslation()

  return (
    <div
      className="flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed bg-card px-4 py-12 text-center"
      data-testid="not-found"
    >
      <h1 className="text-lg font-semibold text-foreground">{title}</h1>
      {description ? <p className="text-sm text-muted-foreground">{description}</p> : null}
      <Button type="button" variant="outline" className="min-h-11 min-w-11" asChild>
        <Link to={linkTo}>{linkLabel ?? t('common.customers')}</Link>
      </Button>
    </div>
  )
}
