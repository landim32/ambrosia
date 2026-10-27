import {
  createContext,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { useTranslation } from 'react-i18next'

import { notifyError, toUserMessage } from '@/lib/errors'
import { IMPORT_POLL_MS } from '@/lib/importForm'
import { ApiError, NotFoundError, UnauthorizedError } from '@/Services/apiClient'
import { importService } from '@/Services/importService'

import type {
  ImportDefaults,
  ImportRun,
  ImportRunDetail,
  ImportRunListQuery,
  ImportRunStartRequest,
} from '@/types/import'
import type { PagedResult } from '@/types/common'

/**
 * Estado da tela de importação (feature 006, research §5/§15).
 *
 * **A senha nunca entra neste contexto** (FR-007, SC-004): ela vive no `useState`
 * local de `ImportPage` e é entregue a `startRun` como campo do corpo da
 * requisição. Nem `defaults`, nem `currentRun`, nem `lastRun`, nem `history` têm
 * credencial — os DTOs de saída da API também não têm — e este contexto não grava
 * nada em `localStorage`, `sessionStorage` ou cookie.
 *
 * O progresso é polling (research §5), não SSE/WebSocket: `GET runs/current` a cada
 * `IMPORT_POLL_MS` enquanto a execução está `queued`/`running`, parado no status
 * final e no desmontar. É o que cumpre o atraso ≤ 5 s (FR-015) sem estado de
 * conexão no gateway nginx.
 */

/** Página inicial do histórico e tamanho padrão (o contrato aceita 1..50). */
const HISTORY_PAGE_SIZE = 10

export interface ImportContextType {
  defaults: ImportDefaults | null
  /** Execução ativa (`queued`/`running`) retomada no mount; `null` quando não há. */
  currentRun: ImportRunDetail | null
  /** Último relatório aberto (o do `202`, do polling final ou de um item do histórico). */
  lastRun: ImportRunDetail | null
  history: PagedResult<ImportRun> | null
  loading: boolean
  /** `true` enquanto o `POST /runs` está em voo (desabilita o botão). */
  starting: boolean
  error: string | null
  loadDefaults: () => Promise<void>
  /** Devolve o resumo criado, ou `null` quando o envio falhou (o erro está em `error`). */
  startRun: (request: ImportRunStartRequest) => Promise<ImportRun | null>
  loadRun: (runId: number) => Promise<void>
  loadHistory: (query?: Partial<ImportRunListQuery>) => Promise<void>
  cancelRun: (runId: number) => Promise<void>
  /** Uma leitura imediata de `runs/current`, sem esperar o próximo tick do polling. */
  refreshCurrent: () => Promise<void>
  /** Para o polling quando a tela de importação desmonta. */
  pausePolling: () => void
  clearError: () => void
}

export const ImportContext = createContext<ImportContextType | undefined>(undefined)

/**
 * `ImportProvider`. Um token de requisição descarta a resposta do ciclo anterior:
 * o polling, o "Atualizar agora" e o cancelamento podem sobrepor leituras, e a
 * tela mostraria um progresso mais velho que o que já chegou.
 */
export const ImportProvider = ({ children }: { children: ReactNode }) => {
  const { t } = useTranslation()
  const translateRef = useRef(t)

  useEffect(() => {
    translateRef.current = t
  }, [t])

  const [defaults, setDefaults] = useState<ImportDefaults | null>(null)
  const [currentRun, setCurrentRun] = useState<ImportRunDetail | null>(null)
  const [lastRun, setLastRun] = useState<ImportRunDetail | null>(null)
  const [history, setHistory] = useState<PagedResult<ImportRun> | null>(null)
  const [loading, setLoading] = useState(false)
  const [starting, setStarting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const currentToken = useRef(0)
  const historyQuery = useRef<ImportRunListQuery>({ page: 1, pageSize: HISTORY_PAGE_SIZE })

  /**
   * O ciclo de leituras da execução atual é um único loop (não um `setInterval`
   * concorrente com o refresh manual), e o `timer` guarda o identificador para o
   * desmontar conseguir parar tudo (FR-015, sem leitura órfã depois do unmount).
   */
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const mounted = useRef(true)

  const clearError = useCallback((): void => {
    setError(null)
  }, [])

  /** Erro que já tem dono fora daqui: 401 pertence ao `AuthContext`, 404 à tela. */
  const absorbError = useCallback((err: unknown): boolean => {
    if (err instanceof UnauthorizedError) return true
    if (err instanceof NotFoundError) return true

    return false
  }, [])

  const stopPolling = useCallback((): void => {
    if (timer.current !== null) {
      clearTimeout(timer.current)
      timer.current = null
    }
  }, [])

  /**
   * Uma leitura de `runs/current`. Devolve o que viu para o chamador decidir se
   * agenda a próxima; `null` quando a leitura falhou ou a resposta foi descartada.
   */
  const readCurrent = useCallback(async (): Promise<ImportRunDetail | null | undefined> => {
    currentToken.current += 1
    const token = currentToken.current

    try {
      const run = await importService.getCurrent()

      if (!mounted.current || currentToken.current !== token) return undefined

      setCurrentRun(run)

      return run
    } catch (err) {
      if (!mounted.current || currentToken.current !== token) return undefined
      if (absorbError(err)) return undefined

      setError(toUserMessage(err, translateRef.current))

      return undefined
    }
  }, [absorbError])

  /**
   * Execução acompanhada pela tela. `runs/current` responde `204` assim que ela
   * sai de `queued`/`running`, então o relatório final não chega pelo polling: com
   * o id guardado aqui, a saída de "ativa" vira uma leitura de `runs/{id}`.
   */
  const trackedRunId = useRef<number | null>(null)

  const finishTracking = useCallback(async (): Promise<void> => {
    const runId = trackedRunId.current
    trackedRunId.current = null

    if (runId === null) return

    try {
      const detail = await importService.getRun(runId)

      if (mounted.current) setLastRun(detail)
    } catch (err) {
      if (!mounted.current || absorbError(err)) return

      setError(toUserMessage(err, translateRef.current))
    }
  }, [absorbError])

  /**
   * Trata o resultado de uma leitura de `runs/current`: execução viva continua
   * no polling; `null` (204) fecha o acompanhamento com o relatório final;
   * `undefined` (falha ou resposta descartada) só para o timer — sem laço de
   * repetição automática contra uma API que está falhando.
   */
  const handleRead = useCallback(
    (run: ImportRunDetail | null | undefined): void => {
      stopPolling()

      if (run === undefined || !mounted.current) return

      if (run === null) {
        void finishTracking()

        return
      }

      trackedRunId.current = run.id
      setLastRun(run)

      timer.current = setTimeout(() => {
        void readCurrent().then((next) => {
          handleReadRef.current(next)
        })
      }, IMPORT_POLL_MS)
    },
    [finishTracking, readCurrent, stopPolling],
  )

  /** O próximo tick sempre usa a versão corrente de `handleRead`. */
  const handleReadRef = useRef(handleRead)

  useEffect(() => {
    handleReadRef.current = handleRead
  }, [handleRead])

  const refreshCurrent = useCallback(async (): Promise<void> => {
    handleRead(await readCurrent())
  }, [handleRead, readCurrent])

  /** A página sai de cena: o polling para (retomado por `loadDefaults` ao voltar, US7-1). */
  const pausePolling = useCallback((): void => {
    currentToken.current += 1
    stopPolling()
  }, [stopPolling])

  const loadDefaults = useCallback(async (): Promise<void> => {
    setLoading(true)

    try {
      const result = await importService.getDefaults()

      if (!mounted.current) return

      setDefaults(result)
      setError(null)

      // O bloqueio vive na conta, não na execução: se `lockedUntil` está no futuro,
      // a tela desabilita o envio sem depender de um 429 para descobrir (FR-008a).
      // Nada a acompanhar no polling, e a execução ativa (se houver) é a próxima
      // leitura.
      await refreshCurrent()
    } catch (err) {
      if (!mounted.current) return
      if (absorbError(err)) return

      setError(toUserMessage(err, translateRef.current))
      notifyError(err, translateRef.current, () => {
        void loadDefaults()
      })
    } finally {
      if (mounted.current) setLoading(false)
    }
  }, [absorbError, refreshCurrent])

  const startRun = useCallback(
    async (request: ImportRunStartRequest): Promise<ImportRun | null> => {
      setStarting(true)
      setError(null)

      try {
        const created = await importService.startRun(request)

        if (!mounted.current) return created

        // O 202 já traz o resumo: a tela mostra "Aguardando" na hora (SC-006) e o
        // polling assume o acompanhamento a partir do id criado — se a execução
        // terminar antes da primeira leitura, o 204 leva direto ao relatório final.
        trackedRunId.current = created.id
        setCurrentRun(null)
        setLastRun(null)

        void refreshCurrent()

        return created
      } catch (err) {
        if (!mounted.current) return null

        // 400/409/429: a mensagem do servidor é o que a tela mostra — o 429 traz o
        // horário de liberação calculado no fuso certo (FR-008a) e o 409 a execução
        // já em andamento (FR-016). Nenhuma dessas mensagens contém credencial.
        if (err instanceof ApiError && err.status === 429) {
          setError(err.mensagem)

          // O bloqueio mudou de estado: os defaults precisam ser relidos para a
          // tela desabilitar o envio com o horário.
          void loadDefaults()

          return null
        }

        if (absorbError(err)) return null

        setError(toUserMessage(err, translateRef.current))

        return null
      } finally {
        if (mounted.current) setStarting(false)
      }
    },
    [absorbError, loadDefaults, refreshCurrent],
  )

  const loadRun = useCallback(
    async (runId: number): Promise<void> => {
      setLoading(true)

      try {
        const detail = await importService.getRun(runId)

        if (!mounted.current) return

        setLastRun(detail)
        setError(null)
      } catch (err) {
        if (!mounted.current) return
        if (absorbError(err)) return

        setError(toUserMessage(err, translateRef.current))
      } finally {
        if (mounted.current) setLoading(false)
      }
    },
    [absorbError],
  )

  const loadHistory = useCallback(
    async (query: Partial<ImportRunListQuery> = {}): Promise<void> => {
      historyQuery.current = {
        page: query.page ?? historyQuery.current.page,
        pageSize: query.pageSize ?? historyQuery.current.pageSize,
      }

      setLoading(true)

      try {
        const page = await importService.listRuns(historyQuery.current)

        if (!mounted.current) return

        setHistory(page)
        setError(null)
      } catch (err) {
        if (!mounted.current) return
        if (absorbError(err)) return

        setError(toUserMessage(err, translateRef.current))
      } finally {
        if (mounted.current) setLoading(false)
      }
    },
    [absorbError],
  )

  const cancelRun = useCallback(
    async (runId: number): Promise<void> => {
      setError(null)

      try {
        const detail = await importService.cancelRun(runId)

        if (!mounted.current) return

        // Em `queued` o 202 já devolve o detalhe final; em `running` o status ainda
        // é `running` e o polling é quem anuncia o `cancelled` ao fim do bloco
        // (FR-019) — por isso o estado é substituído e o loop continua.
        setLastRun(detail)
        setCurrentRun(detail.status === 'cancelled' ? null : detail)
      } catch (err) {
        if (!mounted.current) return

        // 409 "A execução já foi finalizada." é informação, não pane: a mensagem do
        // servidor vai para a tela, sem toast de "tentar de novo".
        if (err instanceof ApiError && err.status === 409) {
          setError(err.mensagem)

          return
        }

        if (absorbError(err)) return

        setError(toUserMessage(err, translateRef.current))
        notifyError(err, translateRef.current)
      }
    },
    [absorbError],
  )

  // O provider vive em `main.tsx` (inclusive em /login, sem sessão): nada é
  // carregado aqui. É a `ImportPage` que chama `loadDefaults` ao montar — o que
  // retoma o progresso ao reabrir a aba (US7-1) — e `pausePolling` ao sair.
  useEffect(() => {
    mounted.current = true

    return () => {
      mounted.current = false
      currentToken.current += 1
      stopPolling()
    }
  }, [stopPolling])

  const value = useMemo<ImportContextType>(
    () => ({
      defaults,
      currentRun,
      lastRun,
      history,
      loading,
      starting,
      error,
      loadDefaults,
      startRun,
      loadRun,
      loadHistory,
      cancelRun,
      refreshCurrent,
      pausePolling,
      clearError,
    }),
    [
      pausePolling,
      defaults,
      currentRun,
      lastRun,
      history,
      loading,
      starting,
      error,
      loadDefaults,
      startRun,
      loadRun,
      loadHistory,
      cancelRun,
      refreshCurrent,
      clearError,
    ],
  )

  return <ImportContext.Provider value={value}>{children}</ImportContext.Provider>
}

export default ImportContext
