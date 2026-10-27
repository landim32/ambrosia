import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ImportProvider } from '@/Contexts/ImportContext'
import { ImportPage } from '@/pages/ImportPage'
import { ApiError } from '@/Services/apiClient'
import { defaultsFixture, detailFixture, runFixture, storageDump } from '@/test/importFixtures'

/**
 * Tela /importacao (T059/T062/T068, FR-007/FR-008a, US2-3, US7-1). O serviço é
 * substituído; só `Date` é falsificado ("hoje" = 27/10/2026).
 */
const PASSWORD = 'SENHA-TESTE-XYZ123'

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

const renderPage = (entry = '/importacao') =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <ImportProvider>
        <ImportPage />
      </ImportProvider>
    </MemoryRouter>,
  )

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-10-27T15:00:00Z'))
  mocks.importService.getDefaults.mockResolvedValue(defaultsFixture())
  mocks.importService.getCurrent.mockResolvedValue(null)
  // Execução que termina antes da primeira leitura: o 204 de runs/current leva ao relatório.
  mocks.importService.getRun.mockResolvedValue(detailFixture())
  mocks.importService.listRuns.mockResolvedValue({ items: [], page: 1, pageSize: 10, totalCount: 0 })
})

afterEach(() => {
  vi.useRealTimers()
})

const submitWithCredentials = async () => {
  const user = userEvent.setup()

  await waitFor(() => {
    expect(screen.getByTestId('import-start-date')).toHaveValue('2026-09-26')
  })
  await user.type(screen.getByTestId('import-login'), 's482+ok')
  await user.type(screen.getByTestId('import-password'), PASSWORD)
  await user.click(screen.getByTestId('import-submit'))
}

const assertPasswordNotRetained = () => {
  expect(screen.getByTestId('import-password')).toHaveValue('')
  expect(storageDump(localStorage)).not.toContain(PASSWORD)
  expect(storageDump(sessionStorage)).not.toContain(PASSWORD)
  expect(document.cookie).not.toContain(PASSWORD)
}

describe('ImportPage', () => {
  it('envia as credenciais uma vez e limpa a senha depois do sucesso', async () => {
    mocks.importService.startRun.mockResolvedValue(runFixture({ status: 'queued' }))

    renderPage()
    await submitWithCredentials()

    await waitFor(() => {
      expect(mocks.importService.startRun).toHaveBeenCalledWith({
        login: 's482+ok',
        password: PASSWORD,
        startDate: '2026-09-26',
        endDate: '2026-10-25',
        dryRun: false,
      })
    })
    assertPasswordNotRetained()
  })

  it('limpa a senha também quando o servidor recusa o início', async () => {
    mocks.importService.startRun.mockRejectedValue(new ApiError(409, 'Já existe importação em andamento.'))

    renderPage()
    await submitWithCredentials()

    expect(await screen.findByTestId('import-server-error')).toHaveTextContent('Já existe importação em andamento.')
    assertPasswordNotRetained()
  })

  it('desabilita o envio e mostra a liberação quando há bloqueio', async () => {
    mocks.importService.getDefaults.mockResolvedValue(defaultsFixture({ lockedUntil: '2026-10-27T18:30:00' }))

    renderPage()

    expect(await screen.findByTestId('import-lock-message')).toHaveTextContent('15:30')
    expect(screen.getByTestId('import-submit')).toBeDisabled()
  })

  it('retoma o progresso da execução ativa ao abrir a tela', async () => {
    mocks.importService.getCurrent.mockResolvedValue(
      detailFixture({ status: 'running', currentBlockNo: 2, blocksFinished: 1 }),
    )

    renderPage()

    expect(await screen.findByTestId('import-block-progress')).toHaveTextContent('Bloco 2 de 5')
    expect(screen.queryByTestId('import-submit')).not.toBeInTheDocument()
  })

  it('pré-preenche o período de "Repetir este bloco" com credenciais vazias', async () => {
    renderPage('/importacao?inicio=2026-10-10&fim=2026-10-16')

    await waitFor(() => {
      expect(screen.getByTestId('import-start-date')).toHaveValue('2026-10-10')
    })
    expect(screen.getByTestId('import-end-date')).toHaveValue('2026-10-16')
    expect(screen.getByTestId('import-login')).toHaveValue('')
    expect(screen.getByTestId('import-password')).toHaveValue('')
  })
})
