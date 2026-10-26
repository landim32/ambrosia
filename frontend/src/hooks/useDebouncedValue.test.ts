import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { useDebouncedValue } from '@/hooks/useDebouncedValue'

/**
 * `useDebouncedValue` (research §14, T048): a busca so dispara depois da pausa
 * de 400 ms na digitacao — menos chamadas a API em rede de loja (FR-009).
 *
 * O avancar dos timers roda dentro de `act`: o atraso dispara um `setState` do
 * React e, sem o envolvimento, a atualizacao nao aparece em `result.current`.
 * `renderHook<Result, Props>` recebe os dois tipos explicitamente para o
 * `initialProps` nao ser estreitado a `{ value: string }` (o caso `undefined`).
 */
interface ProbeProps {
  value: string | undefined
}

const advance = (ms: number): void => {
  act(() => {
    vi.advanceTimersByTime(ms)
  })
}

describe('useDebouncedValue', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('devolve o valor imediatamente na primeira renderizacao', () => {
    vi.useFakeTimers()
    const { result } = renderHook(() => useDebouncedValue('mar'))

    expect(result.current).toBe('mar')
  })

  it('so entrega o novo valor depois do atraso', () => {
    vi.useFakeTimers()
    const { result, rerender } = renderHook<string | undefined, ProbeProps>(
      (props) => useDebouncedValue(props.value),
      { initialProps: { value: 'm' } },
    )

    rerender({ value: 'ma' })
    expect(result.current).toBe('m')

    advance(399)
    expect(result.current).toBe('m')

    advance(1)
    expect(result.current).toBe('ma')
  })

  it('a digitacao continua reinicia a contagem', () => {
    vi.useFakeTimers()
    const { result, rerender } = renderHook<string | undefined, ProbeProps>(
      (props) => useDebouncedValue(props.value),
      { initialProps: { value: 'm' } },
    )

    rerender({ value: 'ma' })
    advance(200)
    rerender({ value: 'mar' })
    advance(200)
    expect(result.current).toBe('m')

    advance(200)
    expect(result.current).toBe('mar')
  })

  it('respeita um atraso customizado', () => {
    vi.useFakeTimers()
    const { result, rerender } = renderHook<string | undefined, ProbeProps>(
      (props) => useDebouncedValue(props.value, 100),
      { initialProps: { value: 'a' } },
    )

    rerender({ value: 'b' })
    advance(99)
    expect(result.current).toBe('a')

    advance(1)
    expect(result.current).toBe('b')
  })

  it('funciona com undefined', () => {
    vi.useFakeTimers()
    const { result, rerender } = renderHook<string | undefined, ProbeProps>(
      (props) => useDebouncedValue(props.value),
      { initialProps: { value: 'x' } },
    )

    rerender({ value: undefined })
    advance(400)
    expect(result.current).toBeUndefined()
  })
})
