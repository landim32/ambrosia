import { useContext } from 'react'

import { ImportContext } from '@/Contexts/ImportContext'

/**
 * Acesso ao estado da importação (skill `react-architecture`).
 *
 * O erro é explícito — sem `undefined` silencioso: esquecer o `ImportProvider` em
 * `main.tsx` quebraria a tela em tempo de execução com uma mensagem inútil, e o
 * custo de escrever o guard aqui é zero.
 */
export const useImport = () => {
  const context = useContext(ImportContext)

  if (!context) {
    throw new Error('useImport deve ser usado dentro de ImportProvider')
  }

  return context
}

export default useImport
