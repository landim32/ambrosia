import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ImportProvider } from '@/Contexts/ImportContext'
import { useImport } from '@/hooks/useImport'
import { IMPORT_POLL_MS } from '@/lib/importForm'
import { defaultsFixture, detailFixture } from '@/test/importFixtures'

import type { ReactNode } from 'react'

/**
 * Estado da importação (T094, FR-015/FR-019, US7-1): retomada da execução atual,
 * polling enquanto `queued`/`running`, relatório final ao sair de ativa e parada
 * do timer — tudo com o serviço substituído e timers falsos.
 */
const mocks = vi.hoisted(() => ({
  importService: {
    getDefaults: vi.fn(),
    startRun: vi.fn(),
    getCurrent: vi.fn(),
    getRun: vi.fn(),
    listRuns: vi.fn(),
    cancelRun: vi.fn(),
  },
}))

vi.mock('@/Services/importService', () => ({ importService: mocks.importService }))

const wrapper = ({ children }: { children: ReactNode }) => <ImportProvider>{children}</ImportProvider>

const flush = async (ms = 0): Promise<void> => {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })
}

beforeEach(() => {
  vi.useFakeTimers()
  mocks.importService.getDefaults.mockResolvedValue(defaultsFixture())
})

afterEach(() => {
  vi.useRealTimers()
})

describe('ImportContext', () => {
  it('não carrega nada sozinho ao montar (o provider vive também em /login)', async () => {
    renderHook(() => useImport(), { wrapper })
    await flush(IMPORT_POLL_MS * 2)

    expect(mocks.importService.getDefaults).not.toHaveBeenCalled()
    expect(mocks.importService.getCurrent).not.toHaveBeenCalled()
  })

  it('retoma a execução atual, faz polling e busca o relatório final', async () => {
    const running = detailFixture({ id: 7, status: 'running', currentBlockNo: 2, blocksFinished: 1 })

    mocks.importService.getCurrent
      .mockResolvedValueOnce(running)
      .mockResolvedValueOnce({ ...running, currentBlockNo: 3, blocksFinished: 2 })
      .mockResolvedValueOnce(null)
    mocks.importService.getRun.mockResolvedValue(detailFixture({ id: 7, status: 'succeeded' }))

    const { result } = renderHook(() => useImport(), { wrapper })

    await act(async () => {
      await result.current.loadDefaults()
    })

    expect(result.current.currentRun?.currentBlockNo).toBe(2)
    expect(mocks.importService.getCurrent).toHaveBeenCalledTimes(1)

    await flush(IMPORT_POLL_MS)
    expect(mocks.importService.getCurrent).toHaveBeenCalledTimes(2)
    expect(result.current.currentRun?.currentBlockNo).toBe(3)

    await flush(IMPORT_POLL_MS)
    expect(mocks.importService.getCurrent).toHaveBeenCalledTimes(3)
    expect(mocks.importService.getRun).toHaveBeenCalledWith(7)
    expect(result.current.lastRun?.status).toBe('succeeded')

    // Status final: o polling parou.
    await flush(IMPORT_POLL_MS * 3)
    expect(mocks.importService.getCurrent).toHaveBeenCalledTimes(3)
  })

  it('cancelRun atualiza o estado com o detalhe devolvido', async () => {
    mocks.importService.getCurrent.mockResolvedValue(null)
    mocks.importService.cancelRun.mockResolvedValue(detailFixture({ id: 9, status: 'cancelled' }))

    const { result } = renderHook(() => useImport(), { wrapper })

    await act(async () => {
      await result.current.cancelRun(9)
    })

    expect(mocks.importService.cancelRun).toHaveBeenCalledWith(9)
    expect(result.current.lastRun?.status).toBe('cancelled')
    expect(result.current.currentRun).toBeNull()
  })

  it('pausePolling interrompe o acompanhamento', async () => {
    mocks.importService.getCurrent.mockResolvedValue(detailFixture({ status: 'running', currentBlockNo: 1 }))

    const { result } = renderHook(() => useImport(), { wrapper })

    await act(async () => {
      await result.current.refreshCurrent()
    })
    act(() => {
      result.current.pausePolling()
    })

    await flush(IMPORT_POLL_MS * 3)
    expect(mocks.importService.getCurrent).toHaveBeenCalledTimes(1)
  })
})
