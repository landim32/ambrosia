import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ImportHistory } from '@/components/imports/ImportHistory'
import { runFixture } from '@/test/importFixtures'

/** Histórico de importações (T094, FR-045/FR-050). */
describe('ImportHistory', () => {
  it('mostra o vazio sem execuções', () => {
    render(<ImportHistory history={null} selectedRunId={null} onOpen={vi.fn()} onPageChange={vi.fn()} />)

    expect(screen.getByText('Nenhuma importação ainda.')).toBeInTheDocument()
  })

  it('lista execuções com destaque de falha/parcial, abre relatório e pagina', async () => {
    const onOpen = vi.fn()
    const onPageChange = vi.fn()

    render(
      <ImportHistory
        history={{
          items: [runFixture({ id: 3, status: 'failed' }), runFixture({ id: 2, status: 'partial' }), runFixture({ id: 1 })],
          page: 1,
          pageSize: 3,
          totalCount: 7,
        }}
        selectedRunId={null}
        onOpen={onOpen}
        onPageChange={onPageChange}
      />,
    )

    expect(screen.getByTestId('import-status-failed')).toHaveTextContent('Falhou')
    expect(screen.getByTestId('import-status-partial')).toHaveTextContent('Parcial')

    const user = userEvent.setup()

    await user.click(screen.getByTestId('history-run-2'))
    expect(onOpen).toHaveBeenCalledWith(2)

    await user.click(screen.getByRole('button', { name: 'Próxima' }))
    expect(onPageChange).toHaveBeenCalledWith(2)
  })
})
