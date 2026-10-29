import { Button } from '@/components/ui/button'
import { Modal } from '@/components/common/Modal'

import type { ReactNode } from 'react'

export interface ConfirmModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Chamado pelo botão de confirmar; a ação é assíncrona e o `busy` segura o botão. */
  onConfirm: () => void
  title: string
  /** Texto de apoio (por que confirmar). Opcional: há confirmação que é só a pergunta. */
  description?: ReactNode
  /** Conteúdo extra antes dos botões — o "simular" do Atualizar base, por exemplo. */
  children?: ReactNode
  confirmLabel: string
  cancelLabel: string
  /** `danger` para o que desfaz cadastro (remover credenciais, FR-006). */
  variant?: 'default' | 'danger'
  /** Rótulo do "X" do diálogo (`aria-label` do `Modal`): vem do i18n, como os demais. */
  closeLabel: string
  /** `true` enquanto a ação está em voo: os dois botões ficam inertes. */
  busy?: boolean
  /** Rótulo do botão de confirmar durante a ação ("Removendo…"). */
  busyLabel?: string
  testId?: string
}

/**
 * Confirmação padrão do app (skill `react-modal`: nunca `window.confirm()`),
 * montada sobre o `Modal` local. O `variant="danger"` usa o `destructive` do
 * `Button` — o vermelho de "isto apaga algo" — e os rótulos vêm por parâmetro,
 * porque em `contracts/ui.md` cada diálogo tem a própria frase em pt-BR.
 *
 * Os botões ficam desabilitados com `busy`: um duplo clique em "Remover" ou em
 * "Iniciar importação" dispararia dois `DELETE`/`POST`, e o segundo chegaria
 * depois do estado já ter mudado.
 */
export const ConfirmModal = ({
  open,
  onOpenChange,
  onConfirm,
  title,
  description,
  children,
  confirmLabel,
  cancelLabel,
  closeLabel,
  variant = 'default',
  busy = false,
  busyLabel,
  testId,
}: ConfirmModalProps) => (
  <Modal
    open={open}
    onOpenChange={(next) => {
      // Fechamento por Esc/"X"/fora enquanto a ação roda seria a vendedora olhando
      // o resultado de um `PUT`/`DELETE` que já não dá para desfazer.
      if (!next && busy) return

      onOpenChange(next)
    }}
    title={title}
    closeLabel={closeLabel}
    footer={
      <>
        <Button
          type="button"
          variant="ghost"
          className="min-h-11"
          disabled={busy}
          onClick={() => {
            onOpenChange(false)
          }}
          data-testid={testId === undefined ? undefined : `${testId}-cancel`}
        >
          {cancelLabel}
        </Button>
        <Button
          type="button"
          variant={variant === 'danger' ? 'destructive' : 'default'}
          className="min-h-11"
          disabled={busy}
          onClick={onConfirm}
          data-testid={testId === undefined ? undefined : `${testId}-confirm`}
        >
          {busy && busyLabel !== undefined ? busyLabel : confirmLabel}
        </Button>
      </>
    }
  >
    {description !== undefined ? <p className="text-sm text-muted-foreground">{description}</p> : null}
    {children}
  </Modal>
)

export default ConfirmModal
