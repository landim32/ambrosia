import { useContext } from 'react'

import { MaisVendasSettingsContext } from '@/Contexts/MaisVendasSettingsContext'

/**
 * Acesso ao estado da configuração do +Vendas (skill `react-architecture`).
 *
 * O erro é explícito — sem `undefined` silencioso, como `useImport`/`useCustomer`:
 * esquecer o `MaisVendasSettingsProvider` em `main.tsx` quebraria a tela em tempo
 * de execução com uma mensagem inútil.
 */
export const useMaisVendasSettings = () => {
  const context = useContext(MaisVendasSettingsContext)

  if (!context) {
    throw new Error('useMaisVendasSettings deve ser usado dentro de MaisVendasSettingsProvider')
  }

  return context
}

export default useMaisVendasSettings
