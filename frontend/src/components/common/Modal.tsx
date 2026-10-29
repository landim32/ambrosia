import { useEffect, useRef } from 'react'

import { X } from 'lucide-react'

import type { ReactNode } from 'react'

export interface ModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Rótulo do diálogo para o leitor de tela (o título visível é `title`). */
  title: string
  /** Corpo do diálogo. */
  children: ReactNode
  /** Ações do rodapé; sem `footer` só o "X" fecha. */
  footer?: ReactNode
  /**
   * Rótulo do "X" e da sobreposição (`aria-label`, então é texto para o leitor de
   * tela). Obrigatório de propósito: nenhuma string visível do app nasce fora do
   * arquivo de i18n (constituição §IV, FR-024).
   */
  closeLabel: string
}

/**
 * Diálogo da aplicação (skill `react-modal`), SEM pacote novo.
 *
 * A skill pede o `Modal` sobre `@radix-ui/react-dialog`; este repo não tem a
 * dependência instalada e o plano da feature 008 declara "Nenhum pacote novo"
 * (`specs/008-import-credentials/plan.md` — Primary Dependencies). Em vez de
 * trazer Radix Dialog contra o plano, o componente replica o MESMO comportamento
 * de diálogo que a folha de "Conta" de `components/layout/TabBar.tsx` já faz no
 * app: sobreposição que fecha no clique de fora, Esc que fecha, foco que entra no
 * diálogo e volta para o elemento de origem ao sair, `role="dialog"` +
 * `aria-modal` + `aria-labelledby`.
 *
 * A armadilha de foco completa (TabSeq) fica de fora de propósito: o diálogo é
 * pequeno, o Esc funciona e o `useEffect` devolve o foco — o ganho de um
 * `FocusTrap` não justifica a dependência aqui. Se Radix Dialog entrar no projeto
 * numa feature futura, este arquivo é o único ponto de troca.
 *
 * O diálogo só é montado quando `open` (não existe no DOM fechado), então o
 * `aria-hidden` do resto da tela não é necessário: o overlay cobre e captura.
 */
export const Modal = ({ open, onOpenChange, title, children, footer, closeLabel }: ModalProps) => {
  /**
   * O elemento que tinha foco antes de abrir, em `ref` e não em `useState`: como o
   * efeito de abertura é o que grava o valor, estado aqui re-renderizaria o diálogo
   * e dispararia o efeito outra vez (o cleanup devolvendo o foco no meio da
   * abertura).
   */
  const previous = useRef<Element | null>(null)
  const closeRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return undefined

    previous.current = document.activeElement
    closeRef.current?.focus()

    const onKey = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') onOpenChange(false)
    }

    window.addEventListener('keydown', onKey)

    return () => {
      window.removeEventListener('keydown', onKey)

      // Devolve o foco ao gatilho: sem isso o leitor de tela fica num elemento que
      // sumiu junto com o diálogo.
      if (previous.current instanceof HTMLElement) previous.current.focus()
      previous.current = null
    }
  }, [onOpenChange, open])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <button
        type="button"
        tabIndex={-1}
        aria-label={closeLabel}
        className="absolute inset-0 bg-black/60 backdrop-blur-[2px]"
        onClick={() => {
          onOpenChange(false)
        }}
      />

      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="relative w-full max-w-md rounded-2xl border border-border bg-popover p-4 shadow-xl"
        data-testid="modal-panel"
      >
        <div className="flex items-start justify-between gap-3">
          <h2 className="text-base font-semibold text-foreground">{title}</h2>
          {/* `<button>` nativo: o `Button` do shadcn não repassa `ref` no React 18. */}
          <button
            ref={closeRef}
            type="button"
            className="inline-flex size-11 shrink-0 items-center justify-center rounded-full bg-secondary text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none"
            aria-label={closeLabel}
            onClick={() => {
              onOpenChange(false)
            }}
          >
            <X aria-hidden="true" className="size-5" />
          </button>
        </div>

        <div className="mt-3 flex flex-col gap-4">{children}</div>

        {footer !== undefined ? <div className="mt-5 flex flex-wrap justify-end gap-2">{footer}</div> : null}
      </div>
    </div>
  )
}

export default Modal
