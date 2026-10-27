import { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

import type { ChangeEvent } from 'react'

export interface PasswordInputProps {
  id: string
  name: string
  value: string
  onChange: (event: ChangeEvent<HTMLInputElement>) => void
  /** `new-password` no cadastro, `current-password` na entrada (FR-020). */
  autoComplete: 'new-password' | 'current-password' | 'off'
  /** Rótulo do botão: `aria-label` do campo ficaria mudo para o leitor de tela. */
  toggleLabels: { show: string; hide: string }
  'aria-describedby'?: string
  'aria-invalid'?: boolean
}

/**
 * Campo de senha com Mostrar/Ocultar (FR-020, `contracts/ui-routes.md`).
 *
 * `type` alterna entre `password` e `text` só na apresentação: o valor nunca sai
 * do estado do formulário, e o botão é `type="button"` para que um clique não
 * submita o formulário (o comportamento que a vendedora espera ao conferir a
 * senha digitada antes de enviar). `aria-pressed` dá o estado ao leitor de tela.
 *
 * Altura `min-h-11` (44 px) nos dois: alvo de toque da FR-019.
 */
export const PasswordInput = ({
  id,
  name,
  value,
  onChange,
  autoComplete,
  toggleLabels,
  ...aria
}: PasswordInputProps) => {
  const [visible, setVisible] = useState(false)

  return (
    <div className="flex items-stretch gap-2">
      <Input
        id={id}
        name={name}
        className="min-h-11"
        type={visible ? 'text' : 'password'}
        autoComplete={autoComplete}
        value={value}
        onChange={onChange}
        {...aria}
      />
      <Button
        type="button"
        variant="outline"
        className="min-h-11 shrink-0 px-3"
        aria-pressed={visible}
        aria-label={visible ? toggleLabels.hide : toggleLabels.show}
        onClick={() => {
          setVisible((current) => !current)
        }}
      >
        {visible ? <EyeOff aria-hidden="true" className="size-4" /> : <Eye aria-hidden="true" className="size-4" />}
        <span className="sr-only">{visible ? toggleLabels.hide : toggleLabels.show}</span>
      </Button>
    </div>
  )
}

export default PasswordInput
