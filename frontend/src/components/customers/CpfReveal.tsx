import { useTranslation } from 'react-i18next'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { maskedCpfShape } from '@/lib/cpf'

export interface CpfRevealProps {
  /**
   * CPF COMPLETO como veio do `CustomerDetailInfo.cpf`. Fica so em memoria e
   * neste componente: nunca vai para storage, log ou listagem (FR-022).
   */
  cpf?: string
  /** `cpfMasked` da API — o unico valor visivel por padrao. */
  cpfMasked: string
}

/**
 * CPF do perfil (FR-012): mascarado por padrao; "Mostrar" revela o completo e o
 * botao vira "Ocultar". O estado revelado e local ao componente e NAO e
 * lembrado — ao desmontar (sair do perfil) volta a mascarado, como exige o Edge
 * Case "CPF revelado".
 *
 * Sem `cpf` (detalhe ainda carregado, ou API sem o campo) o botao nem aparece:
 * nao ha nada a revelar, e nao se fabrica CPF a partir da mascara (T050).
 */
export const CpfReveal = ({ cpf, cpfMasked }: CpfRevealProps) => {
  const { t } = useTranslation()
  const [revealed, setRevealed] = useState(false)

  const hasFullCpf = cpf !== undefined && cpf.trim() !== ''
  const visible = revealed && hasFullCpf ? cpf : maskedCpfShape(cpfMasked)

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="font-mono text-sm text-foreground" data-testid="cpf-value">
        {visible}
      </span>
      {hasFullCpf ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="min-h-11 px-2 text-sm"
          aria-pressed={revealed}
          aria-label={revealed ? `${t('customers.hide')} CPF` : `${t('customers.show')} CPF`}
          onClick={() => {
            setRevealed((current) => !current)
          }}
        >
          {revealed ? t('customers.hide') : t('customers.show')}
        </Button>
      ) : null}
    </div>
  )
}

export default CpfReveal
