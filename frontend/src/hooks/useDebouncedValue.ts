import { useEffect, useState } from 'react'

/**
 * Debounce de valor (research §14, T051).
 *
 * Usado pela busca de clientes e pelos campos de texto dos filtros de pedidos:
 * a consulta so dispara depois de `delay` ms sem mudança, o que corta o numero
 * de chamadas a API na digitacao em rede de loja (FR-009). O valor inicial e
 * entregue ja na primeira renderizacao — nao ha espera no primeiro estado.
 */
export const useDebouncedValue = <TValue,>(value: TValue, delay = 400): TValue => {
  const [debounced, setDebounced] = useState<TValue>(value)

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebounced(value)
    }, delay)

    return () => {
      clearTimeout(timer)
    }
  }, [value, delay])

  return debounced
}

export default useDebouncedValue
