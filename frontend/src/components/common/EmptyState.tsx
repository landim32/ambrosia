import { Button } from '@/components/ui/button'

export interface EmptyStateProps {
  title: string
  /** Descrição opcional abaixo do título. */
  description?: string
  /** Rótulo da ação (ex.: "Limpar busca", "Voltar à primeira página"). */
  actionLabel?: string
  onAction?: () => void
}

/**
 * Lista sem resultados (FR-025). A ação é sempre a correção do próprio filtro:
 * limpar busca/filtros ou voltar à primeira página.
 */
export const EmptyState = ({ title, description, actionLabel, onAction }: EmptyStateProps) => (
  <div
    role="status"
    className="flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed bg-card px-4 py-10 text-center"
    data-testid="empty-state"
  >
    <p className="text-sm font-medium text-foreground">{title}</p>
    {description ? <p className="text-sm text-muted-foreground">{description}</p> : null}
    {actionLabel && onAction ? (
      <Button type="button" variant="outline" className="min-h-11" onClick={onAction}>
        {actionLabel}
      </Button>
    ) : null}
  </div>
)
