import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { MaisVendasSettingsProvider } from '@/Contexts/MaisVendasSettingsContext'
import { MaisVendasCredentialsCard } from '@/components/settings/MaisVendasCredentialsCard'
import { ApiError } from '@/Services/apiClient'
import {
  credentialsFixture,
  credentialsNeedingUpdateFixture,
  noCredentialsFixture,
  storageDump,
} from '@/test/importFixtures'

import type { MaisVendasCredentials } from '@/types/import'

/**
 * Cartão de credenciais do +Vendas (T013 — os cinco cenários de aceitação da US1
 * da spec, FR-001 a FR-007, SC-003).
 *
 * O SERVIÇO é substituído do mesmo jeito de `ImportPage.test.tsx` /
 * `ImportContext.test.tsx` (`vi.hoisted` + `vi.mock` do módulo): o que se prueba
 * aqui é o cartão e o que ele manda embora, não o `fetch`. As asserções de
 * chamada importam: os argumentos do `PUT` são o contrato, e a senha não existir
 * em lugar nenhum é a FR-003.
 *
 * O cartão é montado dentro do `MaisVendasSettingsProvider` VERDADEIRO: a tela é o
 * par contexto-cartão, e mockar o contexto deixaria de fora exatamente o que a
 * feature 008 mudou — o estado que fica (o DTO sem senha) e o que não fica (a
 * senha digitada).
 *
 * Não há `MemoryRouter` aqui: o cartão não tem link (o link da configuração está
 * na tela de importação, FR-009), e um router sem necessidade faria este teste
 * depender de contexto que ele não usa.
 */
const PASSWORD = 'SENHA-TESTE-XYZ123'

const mocks = vi.hoisted(() => ({
  importService: {
    getDefaults: vi.fn(),
    startRun: vi.fn(),
    startUpdateBase: vi.fn(),
    getCredentials: vi.fn(),
    saveCredentials: vi.fn(),
    removeCredentials: vi.fn(),
    getCurrent: vi.fn(),
    getRun: vi.fn(),
    listRuns: vi.fn(),
    cancelRun: vi.fn(),
  },
}))

vi.mock('@/Services/importService', () => ({ importService: mocks.importService }))

/**
 * O toast é a saída de sucesso (contracts/ui.md: 'Credenciais salvas'), mas
 * `sonner` só desenha mensagem com o `<Toaster>` montado — que vive em `main.tsx`,
 * fora desta árvore. Espiar a chamada prova o mesmo fato (a frase foi emitida) sem
 * obrigar o teste a montar o provider global da aplicação.
 */
const toastSpy = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }))

vi.mock('sonner', () => ({ toast: toastSpy }))

const renderCard = () =>
  render(
    <MaisVendasSettingsProvider>
      <MaisVendasCredentialsCard />
    </MaisVendasSettingsProvider>,
  )

/**
 * O campo de senha pelo RÓTULO, e não por `data-testid`: quem desenha o input é o
 * `PasswordInput` comum (o mesmo da entrada e do cadastro), e amarrar o teste a um
 * identificador interno do filho quebraria por uma mudança só de apresentação. O
 * rótulo é contrato: `contracts/ui.md` diz "Senha do +Vendas".
 */
const passwordField = (): HTMLElement => screen.getByLabelText('Senha do +Vendas')

const loginField = (): HTMLElement => screen.getByTestId('settings-login')

/**
 * Monta e ESPERA a leitura terminar. Aguardar `getCredentials` ter sido chamado não
 * basta — a chamada é síncrona no `useEffect` e o estado do cartão só muda quando a
 * promessa resolve, então as asserções seguintes veriam o cartão ainda sem
 * credenciais. A etiqueta de status é o marcador de "a leitura chegou": ela só
 * existe com `credentials !== null` (inclusive no estado `none`).
 */
const withCredentials = async (credentials: MaisVendasCredentials) => {
  mocks.importService.getCredentials.mockResolvedValue(credentials)

  renderCard()

  await screen.findByTestId('credentials-status')
}

beforeEach(() => {
  mocks.importService.saveCredentials.mockResolvedValue(credentialsFixture())
  mocks.importService.removeCredentials.mockResolvedValue(noCredentialsFixture())
})

describe('MaisVendasCredentialsCard', () => {
  it('US1 estado inicial sem credenciais: login vazio, senha vazia e a frase de cadastro', async () => {
    await withCredentials(noCredentialsFixture())

    expect(loginField()).toHaveValue('')
    expect(passwordField()).toHaveValue('')
    expect(
      screen.getByText(
        'Cadastre o login e a senha que você usa no +Vendas. A senha fica guardada protegida e nunca é exibida.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByText('Sem credenciais cadastradas')).toBeInTheDocument()

    // "Remover credenciais" só existe com o que remover (FR-006).
    expect(screen.queryByTestId('settings-remove')).not.toBeInTheDocument()
    expect(screen.getByTestId('settings-save')).toHaveTextContent('Salvar')
  })

  it('US1-2: com credenciais o login vem preenchido, a senha vem VAZIA e a data aparece', async () => {
    await withCredentials(credentialsFixture({ passwordChangedAt: '2026-10-27T15:00:00' }))

    expect(loginField()).toHaveValue('s482+ok')
    expect(passwordField()).toHaveValue('')
    expect(screen.getByTestId('settings-password-saved')).toHaveTextContent(
      'Senha cadastrada em 27/10/2026. Para trocar, digite a nova senha.',
    )
    expect(screen.getByText('Credenciais cadastradas')).toBeInTheDocument()
    expect(screen.getByTestId('settings-remove')).toBeInTheDocument()
  })

  it('o campo de senha é `type="password"` com `autocomplete="new-password"` (contracts/ui.md)', async () => {
    await withCredentials(credentialsFixture())

    expect(passwordField()).toHaveAttribute('type', 'password')
    expect(passwordField()).toHaveAttribute('autocomplete', 'new-password')
    expect(passwordField()).toHaveValue('')
  })

  it('US1-6: nada na tela contém a senha, nem mascarada (FR-003)', async () => {
    await withCredentials(credentialsFixture())

    // O DTO de saída não tem a chave — e o DOM inteiro não tem o valor. Se algum dia
    // a tela ganhar uma senha preenchida, isto pega antes de ir para produção.
    expect(screen.queryByText(PASSWORD)).not.toBeInTheDocument()
    expect(document.body.textContent).not.toContain(PASSWORD)
    expect(document.body.innerHTML).not.toContain(PASSWORD)
    expect(document.body.innerHTML).not.toContain('••••')
  })

  it('US1-1: salva login e senha novos com o corpo do contrato e mostra o toast', async () => {
    const user = userEvent.setup()

    await withCredentials(noCredentialsFixture())

    await user.type(loginField(), 'vendedora@exemplo.com')
    await user.type(passwordField(), PASSWORD)
    await user.click(screen.getByTestId('settings-save'))

    await waitFor(() => {
      expect(mocks.importService.saveCredentials).toHaveBeenCalledWith({
        login: 'vendedora@exemplo.com',
        password: PASSWORD,
      })
    })

    await waitFor(() => {
      expect(toastSpy.success).toHaveBeenCalledWith('Credenciais salvas')
    })

    // Depois de salvar o campo é limpo (contracts/ui.md) e o valor some do DOM.
    expect(passwordField()).toHaveValue('')
    expect(document.body.innerHTML).not.toContain(PASSWORD)
  })

  it('manda o login sem as bordas e a senha tal como foi digitada', async () => {
    const user = userEvent.setup()

    await withCredentials(noCredentialsFixture())

    await user.type(loginField(), '   vendedora@exemplo.com   ')
    await user.type(passwordField(), PASSWORD)
    await user.click(screen.getByTestId('settings-save'))

    await waitFor(() => {
      expect(mocks.importService.saveCredentials).toHaveBeenCalledWith({
        login: 'vendedora@exemplo.com',
        password: PASSWORD,
      })
    })
  })

  it('US1-4: troca só a senha, mantendo o login cadastrado', async () => {
    const user = userEvent.setup()

    await withCredentials(credentialsFixture())

    await user.type(passwordField(), PASSWORD)
    await user.click(screen.getByTestId('settings-save'))

    await waitFor(() => {
      expect(mocks.importService.saveCredentials).toHaveBeenCalledWith({ login: 's482+ok', password: PASSWORD })
    })
  })

  it('com credenciais e senha vazia envia `password: null` ("não mexi na senha")', async () => {
    const user = userEvent.setup()

    await withCredentials(credentialsFixture())

    await user.click(screen.getByTestId('settings-save'))

    await waitFor(() => {
      expect(mocks.importService.saveCredentials).toHaveBeenCalledWith({ login: 's482+ok', password: null })
    })

    // O servidor devolve 200 sem nada mudar (contracts/api.md): nenhum erro na tela.
    expect(screen.queryByTestId('credentials-server-error')).not.toBeInTheDocument()
  })

  it('US1-3: trocar o login sem digitar a senha recusa antes de chamar o serviço', async () => {
    const user = userEvent.setup()

    await withCredentials(credentialsFixture())

    await user.clear(loginField())
    await user.type(loginField(), 'outra-conta+ok')
    await user.click(screen.getByTestId('settings-save'))

    expect(screen.getByText('Informe a senha para trocar o login.')).toBeInTheDocument()
    expect(mocks.importService.saveCredentials).not.toHaveBeenCalled()

    // A frase é a mesma do `PUT` (contracts/api.md) e o campo fica marcado.
    expect(passwordField()).toHaveAttribute('aria-invalid', 'true')
    expect(passwordField()).toHaveAccessibleDescription('Informe a senha para trocar o login.')
  })

  it('sem credenciais a senha é obrigatória por outro motivo: é o primeiro cadastro', async () => {
    const user = userEvent.setup()

    await withCredentials(noCredentialsFixture())

    await user.type(loginField(), 'vendedora@exemplo.com')
    await user.click(screen.getByTestId('settings-save'))

    expect(screen.getByText('Informe a senha do +Vendas.')).toBeInTheDocument()
    expect(mocks.importService.saveCredentials).not.toHaveBeenCalled()
  })

  it('bloqueia login vazio', async () => {
    const user = userEvent.setup()

    await withCredentials(noCredentialsFixture())

    await user.click(screen.getByTestId('settings-save'))

    expect(screen.getByText('Informe o login do +Vendas.')).toBeInTheDocument()
    expect(loginField()).toHaveAttribute('aria-invalid', 'true')
    expect(mocks.importService.saveCredentials).not.toHaveBeenCalled()
  })

  it('mostra a mensagem do servidor no 400, sem reescrevê-la, e descarta a senha', async () => {
    const user = userEvent.setup()

    mocks.importService.saveCredentials.mockRejectedValue(
      new ApiError(400, 'O login deve ter no máximo 120 caracteres.', [
        { campo: 'login', mensagem: 'O login deve ter no máximo 120 caracteres.' },
      ]),
    )

    await withCredentials(credentialsFixture())

    await user.type(passwordField(), PASSWORD)
    await user.click(screen.getByTestId('settings-save'))

    expect(await screen.findByTestId('credentials-server-error')).toHaveTextContent(
      'O login deve ter no máximo 120 caracteres.',
    )

    // Sem toast de sucesso, e a senha digitada foi descartada mesmo com a recusa:
    // o `PUT` já a levou, e deixá-la na tela seria retenção (FR-003).
    expect(toastSpy.success).not.toHaveBeenCalled()
    expect(passwordField()).toHaveValue('')
  })

  it('FR-007: `needsUpdate` mostra o alerta de senha recusada', async () => {
    await withCredentials(credentialsNeedingUpdateFixture())

    expect(screen.getByTestId('credentials-needs-update')).toBeInTheDocument()
    expect(
      screen.getByText('O +Vendas recusou a senha cadastrada. Digite a senha atual para continuar importando.'),
    ).toBeInTheDocument()
    expect(screen.getByText('Credenciais a atualizar')).toBeInTheDocument()
    // Alerta não é bloqueio: dá para digitar a senha nova.
    expect(screen.getByTestId('settings-save')).toBeEnabled()
  })

  it('a senha digitada não vai para storage nem cookie, nem depois de fechar a tela', async () => {
    const user = userEvent.setup()

    mocks.importService.getCredentials.mockResolvedValue(noCredentialsFixture())

    const { unmount } = renderCard()

    await waitFor(() => {
      expect(mocks.importService.getCredentials).toHaveBeenCalled()
    })

    await user.type(passwordField(), PASSWORD)

    unmount()

    expect(storageDump(localStorage)).not.toContain(PASSWORD)
    expect(storageDump(sessionStorage)).not.toContain(PASSWORD)
    expect(document.cookie).not.toContain(PASSWORD)
  })

  it('US1-5: remover pede confirmação num diálogo e chama o DELETE uma vez', async () => {
    const user = userEvent.setup()

    await withCredentials(credentialsFixture())

    await user.click(screen.getByTestId('settings-remove'))

    // Skill `react-modal`: nada de `window.confirm` — a confirmação é DOM da tela.
    expect(screen.getByRole('dialog', { name: 'Remover as credenciais do +Vendas?' })).toBeInTheDocument()
    expect(mocks.importService.removeCredentials).not.toHaveBeenCalled()

    await user.click(screen.getByTestId('settings-remove-modal-cancel'))

    expect(mocks.importService.removeCredentials).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await user.click(screen.getByTestId('settings-remove'))
    await user.click(screen.getByTestId('settings-remove-modal-confirm'))

    await waitFor(() => {
      expect(mocks.importService.removeCredentials).toHaveBeenCalledTimes(1)
    })
  })

  it('US1-5 / FR-006: depois de remover o cartão volta ao estado sem credenciais', async () => {
    const user = userEvent.setup()

    await withCredentials(credentialsFixture())

    await user.click(screen.getByTestId('settings-remove'))
    await user.click(screen.getByTestId('settings-remove-modal-confirm'))

    await waitFor(() => {
      expect(toastSpy.success).toHaveBeenCalledWith('Credenciais removidas')
    })

    expect(loginField()).toHaveValue('')
    expect(passwordField()).toHaveValue('')
    expect(screen.getByText('Sem credenciais cadastradas')).toBeInTheDocument()
    expect(screen.queryByTestId('settings-remove')).not.toBeInTheDocument()
  })

  it('um `DELETE` que devolve 204 (`null` no corpo) também zera o cartão', async () => {
    const user = userEvent.setup()

    mocks.importService.removeCredentials.mockResolvedValue(null)

    await withCredentials(credentialsFixture())

    await user.click(screen.getByTestId('settings-remove'))
    await user.click(screen.getByTestId('settings-remove-modal-confirm'))

    await waitFor(() => {
      expect(screen.getByText('Sem credenciais cadastradas')).toBeInTheDocument()
    })
    expect(loginField()).toHaveValue('')
  })

  it('nota de segurança na tela: a senha nunca é exibida (FR-003)', async () => {
    await withCredentials(credentialsFixture())

    expect(
      screen.getByText(
        'A senha fica guardada cifrada no servidor e nunca é exibida, nem mascarada: para trocar, digite a nova.',
      ),
    ).toBeInTheDocument()
  })
})
