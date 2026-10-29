import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ImportProvider } from '@/Contexts/ImportContext'
import { ImportPage } from '@/pages/ImportPage'
import { ApiError } from '@/Services/apiClient'
import {
  defaultsFixture,
  defaultsNeedingUpdateFixture,
  defaultsWithoutCredentialsFixture,
  detailFixture,
  runFixture,
  storageDump,
} from '@/test/importFixtures'

/**
 * Tela /importacao (T059/T062/T068 da 006; T027 da 008, FR-009/FR-008a/FR-013,
 * US2-1/US2-2, SC-002). O serviço é substituído; só `Date` é falsificado
 * ("hoje" = 27/10/2026).
 *
 * A feature 008 mudou a natureza desta prova: antes era "a senha é enviada uma vez
 * e apagada"; agora é "não existe campo de senha para enviar" — o corpo do `POST`
 * leva só as datas, e a tela fala das credenciais apenas pelo estado
 * `hasCredentials`/`credentialsStatus` dos defaults.
 */
const SETTINGS_PATH = '/configuracoes/mais-vendas'
const PASSWORD = 'SENHA-TESTE-XYZ123'

const mocks = vi.hoisted(() => ({
  importService: {
    getDefaults: vi.fn(),
    startRun: vi.fn(),
    startUpdateBase: vi.fn(),
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

/** Espera os defaults chegarem (o formulário só é desenhado com as datas sugeridas). */
const awaitPeriod = async () => {
  const user = userEvent.setup()

  await waitFor(() => {
    expect(screen.getByTestId('import-start-date')).toHaveValue('2026-09-26')
  })

  return user
}

/** A tela não tem campo de credencial em lugar nenhum (FR-009, SC-003). */
const assertNoCredentialInput = () => {
  expect(screen.queryByTestId('import-login')).not.toBeInTheDocument()
  expect(screen.queryByTestId('import-password')).not.toBeInTheDocument()
  expect(document.querySelectorAll('input[type="password"]')).toHaveLength(0)
  expect(storageDump(localStorage)).not.toContain(PASSWORD)
  expect(storageDump(sessionStorage)).not.toContain(PASSWORD)
  expect(document.cookie).not.toContain(PASSWORD)
}

describe('ImportPage', () => {
  it('inicia só com as datas, sem nenhum campo de credencial', async () => {
    mocks.importService.startRun.mockResolvedValue(runFixture({ status: 'queued' }))

    renderPage()
    const user = await awaitPeriod()

    await user.click(screen.getByTestId('import-submit'))

    await waitFor(() => {
      expect(mocks.importService.startRun).toHaveBeenCalledWith({
        startDate: '2026-09-26',
        endDate: '2026-10-25',
        dryRun: false,
      })
    })
    assertNoCredentialInput()
  })

  it('mostra a recusa do servidor quando falta credencial, sem reescrevê-la', async () => {
    mocks.importService.startRun.mockRejectedValue(
      new ApiError(400, 'Cadastre as credenciais do +Vendas na configuração.', []),
    )

    renderPage()
    const user = await awaitPeriod()

    await user.click(screen.getByTestId('import-submit'))

    // A frase é a do contrato (`contracts/api.md`), repetida na tela tal como veio.
    expect(await screen.findByTestId('import-server-error')).toHaveTextContent(
      'Cadastre as credenciais do +Vendas na configuração.',
    )
    assertNoCredentialInput()
  })

  it('sem credenciais troca o formulário pelo aviso com o link da configuração (FR-009)', async () => {
    mocks.importService.getDefaults.mockResolvedValue(defaultsWithoutCredentialsFixture())

    renderPage()

    expect(await screen.findByTestId('import-no-credentials')).toBeInTheDocument()
    expect(screen.getByText('Cadastre as credenciais do +Vendas para importar.')).toBeInTheDocument()
    expect(screen.getByTestId('import-go-to-settings')).toHaveAttribute('href', SETTINGS_PATH)
    expect(screen.getByTestId('import-submit')).toBeDisabled()
    assertNoCredentialInput()
  })

  it('com credenciais a atualizar avisa com o link e deixa iniciar (FR-007)', async () => {
    mocks.importService.getDefaults.mockResolvedValue(defaultsNeedingUpdateFixture())
    mocks.importService.startRun.mockResolvedValue(runFixture({ status: 'queued' }))

    renderPage()
    const user = await awaitPeriod()

    expect(screen.getByTestId('import-needs-update')).toBeInTheDocument()
    expect(screen.getByTestId('import-go-to-settings')).toHaveAttribute('href', SETTINGS_PATH)

    await user.click(screen.getByTestId('import-submit'))

    await waitFor(() => {
      expect(mocks.importService.startRun).toHaveBeenCalled()
    })
  })

  it('"Atualizar base" manda só o modo e o período vem do servidor (FR-013)', async () => {
    mocks.importService.startUpdateBase.mockResolvedValue(
      runFixture({ status: 'queued', kind: 'updateBase' }),
    )

    renderPage()
    const user = await awaitPeriod()

    await user.click(screen.getByTestId('import-update-base'))
    expect(screen.getByText('Importar de 26/09/2026 a 25/10/2026?')).toBeInTheDocument()

    await user.click(screen.getByTestId('import-update-base-modal-confirm'))

    await waitFor(() => {
      expect(mocks.importService.startUpdateBase).toHaveBeenCalledWith({ dryRun: false })
    })
    expect(mocks.importService.startRun).not.toHaveBeenCalled()
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

  it('mostra no progresso a execução iniciada pelo plugin (SC-002)', async () => {
    mocks.importService.getCurrent.mockResolvedValue(
      detailFixture({ status: 'running', startedVia: 'mcp', currentBlockNo: 1, blocksFinished: 0 }),
    )

    renderPage()

    expect(await screen.findByTestId('import-via-plugin')).toHaveTextContent('Pelo plugin')
    expect(screen.getByTestId('import-started-externally')).toHaveTextContent(
      'Esta importação foi iniciada pelo plugin do ChatGPT e continua aqui.',
    )
  })

  it('marca no histórico a execução do plugin e o "Atualizar base" (research R8)', async () => {
    mocks.importService.listRuns.mockResolvedValue({
      items: [
        runFixture({ id: 7, startedVia: 'mcp' }),
        runFixture({ id: 6, kind: 'updateBase' }),
        runFixture({ id: 5 }),
      ],
      page: 1,
      pageSize: 10,
      totalCount: 3,
    })

    renderPage()

    const fromPlugin = await screen.findByTestId('history-run-7')

    expect(fromPlugin).toHaveTextContent('Pelo plugin')

    const updateBase = screen.getByTestId('history-run-6')

    expect(updateBase).toHaveTextContent('Atualizar base')

    // A execução normal da tela não ganha rótulo nenhum (feature 006 igual).
    expect(screen.getByTestId('history-run-5')).not.toHaveTextContent('Pelo plugin')
  })

  it('pré-preenche o período de "Repetir este bloco"', async () => {
    renderPage('/importacao?inicio=2026-10-10&fim=2026-10-16')

    await waitFor(() => {
      expect(screen.getByTestId('import-start-date')).toHaveValue('2026-10-10')
    })
    expect(screen.getByTestId('import-end-date')).toHaveValue('2026-10-16')
    assertNoCredentialInput()
  })
})
