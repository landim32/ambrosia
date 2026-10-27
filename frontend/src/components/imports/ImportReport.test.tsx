import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { ImportReport } from '@/components/imports/ImportReport'
import { blockFixture, counts, detailFixture } from '@/test/importFixtures'

import type { ImportRunDetail } from '@/types/import'

/** Relatório da execução (T041/T070/T079, FR-047 a FR-051). */
const Probe = () => {
  const location = useLocation()

  return <div data-testid="probe">{`${location.pathname}${location.search}`}</div>
}

const renderReport = (run: ImportRunDetail) =>
  render(
    <MemoryRouter initialEntries={['/relatorio']}>
      <Routes>
        <Route path="/relatorio" element={<ImportReport run={run} />} />
        <Route path="*" element={<Probe />} />
      </Routes>
    </MemoryRouter>,
  )

const succeeded = () =>
  detailFixture({
    changes: [
      {
        orderId: 101,
        orderNumber: '41900001',
        orderDate: '2026-09-28',
        kind: 'sale',
        channel: 'Linx',
        origin: 'cota',
        action: 'inserted',
        changedFields: null,
        errorMessage: null,
      },
      {
        orderId: 102,
        orderNumber: '41900004',
        orderDate: '2026-09-29',
        kind: 'sale',
        channel: 'Online',
        origin: 'omni',
        action: 'updated',
        changedFields: ['total', 'status'],
        errorMessage: null,
      },
    ],
    breakdown: [{ channel: 'Linx', origin: 'cota', kind: 'sale', action: 'inserted', count: 26 }],
  })

describe('ImportReport', () => {
  it('mostra resultado, modo, período, duração, contagens e agrupamento', () => {
    renderReport(succeeded())

    expect(screen.getByTestId('import-status-succeeded')).toHaveTextContent('Concluída')
    expect(screen.getByText('Gravação')).toBeInTheDocument()
    expect(screen.getByText('26/09/2026 a 25/10/2026')).toBeInTheDocument()
    expect(screen.getByText('59 s')).toBeInTheDocument()
    expect(screen.getByTestId('report-count-inserted')).toHaveTextContent('49')
    expect(screen.getByTestId('import-breakdown')).toHaveTextContent('Linx · Cota · Venda · Inserido: 26')
  })

  it('liga cada pedido a /pedidos/{orderId} e mostra os campos alterados', async () => {
    renderReport(succeeded())

    expect(screen.getByText('Alterados: total, status')).toBeInTheDocument()

    await userEvent.setup().click(screen.getByRole('link', { name: 'Abrir pedido 41900001' }))

    expect(screen.getByTestId('probe')).toHaveTextContent('/pedidos/101')
  })

  it('bloco falho mostra motivo, pedidos gravados e "Repetir este bloco"', async () => {
    renderReport(
      detailFixture({
        status: 'failed',
        errorMessage: 'Falha inesperada ao gravar o pedido na base.',
        blocks: [
          blockFixture(1, { counts: counts({ inserted: 8 }) }),
          blockFixture(2, { counts: counts({ inserted: 8 }) }),
          blockFixture(3, {
            status: 'failed',
            counts: counts({ inserted: 14 }),
            errorMessage: 'Falha inesperada ao gravar o pedido na base.',
          }),
          blockFixture(4, { status: 'notExecuted' }),
          blockFixture(5, { status: 'notExecuted' }),
        ],
      }),
    )

    const block3 = screen.getByTestId('report-block-3')

    expect(within(block3).getByText('14 pedidos gravados')).toBeInTheDocument()
    expect(within(block3).getByText('Falha inesperada ao gravar o pedido na base.')).toBeInTheDocument()
    expect(screen.getByTestId('import-status-failed')).toBeInTheDocument()

    await userEvent.setup().click(screen.getByTestId('repeat-block-3'))

    expect(screen.getByTestId('probe')).toHaveTextContent('/importacao?inicio=2026-10-10&fim=2026-10-16')
  })

  it('identifica a simulação, usa "seriam…" e não liga pedidos', () => {
    const base = succeeded()

    renderReport({
      ...base,
      mode: 'dryRun',
      changes: base.changes.map((change) => ({ ...change, orderId: null })),
    })

    expect(screen.getByTestId('import-simulation-tag')).toHaveTextContent('Simulação — nada foi gravado')
    expect(screen.getByText('Seriam inseridos')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Abrir pedido/ })).not.toBeInTheDocument()
  })

  it('avisa queda de contagem frente à execução anterior', () => {
    renderReport(detailFixture({ countDropWarning: true, previousRunOrdersCount: 49 }))

    expect(screen.getByTestId('import-count-drop')).toHaveTextContent('(49)')
  })

  it('destaca execução parcial', () => {
    renderReport(detailFixture({ status: 'partial' }))

    expect(screen.getByTestId('import-status-partial')).toHaveTextContent('Parcial')
  })
})
