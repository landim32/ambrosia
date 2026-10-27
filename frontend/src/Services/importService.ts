import { ApiError, apiRequest } from '@/Services/apiClient'

import type { PagedResult } from '@/types/common'
import type {
  ImportDefaults,
  ImportRun,
  ImportRunDetail,
  ImportRunListQuery,
  ImportRunStartRequest,
} from '@/types/import'

/**
 * Import Service — importação do +Vendas (feature 006, skill `react-architecture`:
 * classe singleton sobre `apiRequest`).
 *
 * Seis métodos, um por rota do contrato `contracts/imports-api.md`. Nada aqui
 * guarda credencial: `startRun` recebe o corpo, envia e esquece — o objeto
 * `ImportRun`/`ImportRunDetail` devolvido pela API não tem `login` nem `password`
 * (FR-006), e o serviço não devolve o que enviou.
 *
 * O 429 de bloqueio (FR-008a) é tratado como erro de negócio com a mensagem do
 * servidor: `apiRequest` já monta `ApiError(status, mensagem, erros)`, e a tela
 * mostra exatamente o texto que veio — inclusive o horário de liberação, que só o
 * servidor sabe calcular no fuso certo.
 */
class ImportService {
  /** `GET /imports/mais-vendas/defaults` (FR-002/FR-008a). */
  async getDefaults(): Promise<ImportDefaults> {
    return apiRequest<ImportDefaults>('GET', '/imports/mais-vendas/defaults')
  }

  /**
   * `POST /imports/mais-vendas/runs` → `202` com a execução `queued` (FR-001).
   * O corpo é o único lugar do frontend que transporta a senha, e ele não é
   * retido: quem chama passa o objeto e o descarta (T068: a página zera o campo).
   */
  async startRun(request: ImportRunStartRequest): Promise<ImportRun> {
    return apiRequest<ImportRun>('POST', '/imports/mais-vendas/runs', { body: request })
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
