import { ApiError, apiRequest } from '@/Services/apiClient'

import type { PagedResult } from '@/types/common'
import type {
  ImportDefaults,
  ImportRun,
  ImportRunDetail,
  ImportRunListQuery,
  ImportRunStartRequest,
  ImportUpdateBaseRequest,
  MaisVendasCredentials,
  MaisVendasCredentialsRequest,
} from '@/types/import'

/** As três rotas de credenciais (`GET`/`PUT`/`DELETE`) dividem este caminho (contracts/api.md). */
const CREDENTIALS_PATH = '/imports/mais-vendas/credentials'

/**
 * Import Service — importação do +Vendas e credenciais guardadas (feature 006 e
 * 008, skill `react-architecture`: classe singleton sobre `apiRequest`).
 *
 * Um serviço por área de API, como `customerService` cobre `/customers/{id}`: as
 * credenciais moram em `/imports/mais-vendas/credentials` — mesma área, mesmo
 * consumidor — e separá-las num serviço próprio só dividiria o mock dos testes
 * (research R4: "mantém tudo sob o recurso de importação, que é o único
 * consumidor").
 *
 * Nada aqui GUARDA credencial: `saveCredentials` recebe o corpo, envia e esquece,
 * e devolve apenas o `MaisVendasCredentialsInfo` do servidor, que não tem campo de
 * senha (FR-003). O serviço também não devolve o que recebeu.
 *
 * O 429 de bloqueio (FR-008a) é tratado como erro de negócio com a mensagem do
 * servidor: `apiRequest` já monta `ApiError(status, mensagem, erros)`, e a tela
 * mostra exatamente o texto que veio — inclusive o horário de liberação, que só o
 * servidor sabe calcular no fuso certo.
 */
class ImportService {
  /** `GET /imports/mais-vendas/defaults` (FR-002/FR-008a + 008 R5). */
  async getDefaults(): Promise<ImportDefaults> {
    return apiRequest<ImportDefaults>('GET', '/imports/mais-vendas/defaults')
  }

  /**
   * `POST /imports/mais-vendas/runs` → `202` com a execução `queued` (FR-001,
   * feature 008: corpo só com datas e modo — login e senha saíram do contrato e
   * um campo extra é recusado com 400, então nada aqui os transporta).
   */
  async startRun(request: ImportRunStartRequest): Promise<ImportRun> {
    return apiRequest<ImportRun>('POST', '/imports/mais-vendas/runs', { body: request })
  }

  /**
   * `POST /imports/mais-vendas/runs/update-base` → `202` com a execução
   * `kind: "updateBase"` (FR-013). O período é calculado pelo SERVIDOR — a tela só
   * repete o que veio nos defaults na confirmação —, então um 400 aqui traz a
   * recusa da qual a tela não tem cópia local (sem credenciais, sem pedido
   * importado, intervalo acima do limite): `apiRequest` transforma em `ApiError`
   * e a mensagem do servidor é a que aparece.
   */
  async startUpdateBase(request: ImportUpdateBaseRequest): Promise<ImportRun> {
    return apiRequest<ImportRun>('POST', '/imports/mais-vendas/runs/update-base', { body: request })
  }

  /**
   * `GET /imports/mais-vendas/credentials` (FR-001/FR-004): o login cadastrado e
   * se há senha, nunca qual senha (FR-003).
   */
  async getCredentials(): Promise<MaisVendasCredentials> {
    return apiRequest<MaisVendasCredentials>('GET', CREDENTIALS_PATH)
  }

  /**
   * `PUT /imports/mais-vendas/credentials` → a mesma forma do `GET` (contracts/api.md).
   * `password: null` significa "mantém a senha guardada" — a troca de login exige
   * senha, e é o servidor quem diz isso com a mensagem dele. A senha em claro
   * atravessa esta chamada e morre aqui: não volta no retorno, não vai para estado
   * nem para storage (FR-003, SC-003).
   */
  async saveCredentials(request: MaisVendasCredentialsRequest): Promise<MaisVendasCredentials> {
    return apiRequest<MaisVendasCredentials>('PUT', CREDENTIALS_PATH, { body: request })
  }

  /**
   * `DELETE /imports/mais-vendas/credentials` (FR-006): idempotente, nunca 404. O
   * servidor devolve a forma do `GET` já zerada (`hasPassword: false`,
   * `status: 'none'`); o `204` histórico do contrato devolve `undefined`, e o
   * chamador trata `null` como "sem credenciais" — as duas respostas saem da tela
   * com o mesmo estado (T013 "remover credenciais").
   */
  async removeCredentials(): Promise<MaisVendasCredentials | null> {
    const result = await apiRequest<MaisVendasCredentials | undefined>('DELETE', CREDENTIALS_PATH)

    return result ?? null
  }

  /**
   * `GET /imports/mais-vendas/runs/current`: a execução ativa, ou `null` no `204`
   * sem execução (o `apiRequest` devolve `undefined` em 204 por contrato).
   */
  async getCurrent(): Promise<ImportRunDetail | null> {
    const result = await apiRequest<ImportRunDetail | undefined>('GET', '/imports/mais-vendas/runs/current')

    return result ?? null
  }

  /** `GET /imports/mais-vendas/runs/{runId}` → relatório. 404 vira `NotFoundError`. */
  async getRun(runId: number): Promise<ImportRunDetail> {
    return apiRequest<ImportRunDetail>('GET', `/imports/mais-vendas/runs/${String(runId)}`)
  }

  /** `GET /imports/mais-vendas/runs?page&pageSize` → histórico paginado (FR-045). */
  async listRuns(query: ImportRunListQuery): Promise<PagedResult<ImportRun>> {
    return apiRequest<PagedResult<ImportRun>>('GET', '/imports/mais-vendas/runs', {
      // pageSize da importação é o que a tela pediu (1..50), não o PAGE_SIZE=20
      // default do helper: o histórico tem linhas largas e a paginação própria é
      // do contrato. `buildQuery` só injeta 20 quando a chave está ausente.
      query: { page: query.page, pageSize: query.pageSize },
    })
  }

  /**
   * `POST /imports/mais-vendas/runs/{runId}/cancel` → `202` com o detalhe e
   * `cancelRequested: true` (FR-019). 409 ("já finalizada") chega como `ApiError`
   * e é mensagem de tela, não exceção de fluxo.
   */
  async cancelRun(runId: number): Promise<ImportRunDetail> {
    return apiRequest<ImportRunDetail>('POST', `/imports/mais-vendas/runs/${String(runId)}/cancel`)
  }

  /**
   * A mensagem do bloqueio (429) para a tela: o texto vem pronto do servidor, com
   * o horário em São Paulo (FR-008a). Fora do 429 devolve `null` para o chamador
   * usar o caminho de erro comum.
   */
  lockMessageOf(err: unknown): string | null {
    return err instanceof ApiError && err.status === 429 ? err.mensagem : null
  }
}

export const importService = new ImportService()
export default ImportService
