import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ImportProgress } from '@/components/imports/ImportProgress'
import { blockFixture, counts, detailFixture } from '@/test/importFixtures'

/** Progresso da execução (T040, FR-014/FR-017/FR-019/FR-032). */
const running = () =>
  detailFixture({
    status: 'running',
    blocksFinished: 2,
    currentBlockNo: 3,
    finishedAt: null,
    durationSeconds: null,
    counts: counts({ inserted: 16, customersCreated: 7 }),
    blocks: [
      blockFixture(1, { counts: counts({ inserted: 8 }) }),
      blockFixture(2, { counts: counts({ inserted: 8 }) }),
      blockFixture(3, { status: 'running', finishedAt: null }),
      blockFixture(4, { status: 'pending', startedAt: null, finishedAt: null }),
      blockFixture(5, { status: 'pending', startedAt: null, finishedAt: null }),
    ],
  })

describe('ImportProgress', () => {
  it('mostra o bloco em andamento, as contagens parciais e o status de cada bloco', () => {
    render(<ImportProgress run={running()} onCancel={vi.fn()} onRefresh={vi.fn()} />)

    expect(screen.getByTestId('import-block-progress')).toHaveTextContent('Bloco 3 de 5')
    expect(screen.getByTestId('count-inserted')).toHaveTextContent('16')
    expect(screen.getByTestId('count-customersCreated')).toHaveTextContent('7')
    expect(screen.getByTestId('progress-block-1')).toHaveTextContent('8 pedidos gravados')
    expect(screen.getAllByTestId('import-block-status-pending')).toHaveLength(2)
  })

  it('mostra "Aguardando" e blocos concluídos/total quando na fila', () => {
    render(
      <ImportProgress
        run={detailFixture({ status: 'queued', blocksFinished: 0, currentBlockNo: null })}
        onCancel={vi.fn()}
        onRefresh={vi.fn()}
      />,
    )

    expect(screen.getByTestId('import-queued')).toHaveTextContent('Aguardando')
    expect(screen.getByTestId('import-block-progress')).toHaveTextContent('0 de 5 blocos concluídos')
  })

  it('usa os textos "seriam…" em simulação', () => {
    render(<ImportProgress run={{ ...running(), mode: 'dryRun' }} onCancel={vi.fn()} onRefresh={vi.fn()} />)

    expect(screen.getByText('Seriam inseridos')).toBeInTheDocument()
    expect(screen.getByText('Simulação — nada foi gravado')).toBeInTheDocument()
  })

  it('aciona o cancelamento', async () => {
    const onCancel = vi.fn()

    render(<ImportProgress run={running()} onCancel={onCancel} onRefresh={vi.fn()} />)
    await userEvent.setup().click(screen.getByTestId('import-cancel'))

    expect(onCancel).toHaveBeenCalledTimes(1)
  })
})
