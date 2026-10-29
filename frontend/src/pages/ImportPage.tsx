import { useCallback, useEffect, useMemo, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { toast } from 'sonner'

import { LoadingList } from '@/components/common/LoadingList'
import { ImportForm } from '@/components/imports/ImportForm'
import { ImportHistory } from '@/components/imports/ImportHistory'
import { ImportProgress } from '@/components/imports/ImportProgress'
import { ImportReport } from '@/components/imports/ImportReport'
import { useImport } from '@/hooks/useImport'
import { periodFromParams, type ImportFormValues } from '@/lib/importForm'
import { isImportRunActive } from '@/types/import'

/**
 * Tela `/importacao` (feature 006, US1/US2/US3/US4/US7; feature 008, US2/US3).
 *
 * Fluxo: ao montar, busca os defaults (datas sugeridas, limite em meses, situação
 * das credenciais e o período do "Atualizar base") e retoma a execução ativa, se
 * houver — fechar e reabrir a aba volta ao progresso (US7-1), porque a execução
 * vive no servidor. Com execução ativa a tela mostra o progresso no lugar do
 * formulário (um segundo início seria 409, FR-016); ao terminar, mostra o relatório
 * e atualiza o histórico.
 *
 * **Nenhum campo de credencial aqui** (FR-009): sem cadastro, o formulário dá lugar
 * ao aviso com o link da configuração, dentro do `ImportForm`. O corpo do `POST`
 * leva só as datas e o modo, então não existe senha a descartar nesta página — a
 * garantia que a feature 006 fazia limpar o campo passou a ser do contrato.
 *
 * `?inicio=AAAA-MM-DD&fim=AAAA-MM-DD` é o destino de "Repetir este bloco"
 * (FR-026): pré-preenche o período. Nada de credencial vem por URL (FR-007 da 006,
 * FR-003 da 008).
 *
 * A execução iniciada pelo PLUGIN do ChatGPT (FR-016, SC-002) aparece aqui pelo
 * mesmo polling de `runs/current` — `startedVia: 'mcp'` é o que a distingue, e o
 * rótulo "Pelo plugin" vai no progresso e no histórico (T027).
 */
export const ImportPage = () => {
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()
  const {
    defaults,
    currentRun,
    lastRun,
    history,
    loading,
    starting,
    error,
    loadDefaults,
    startRun,
    startUpdateBase,
    loadRun,
    loadHistory,
    cancelRun,
    refreshCurrent,
    pausePolling,
    clearError,
  } = useImport()

  const initialPeriod = useMemo(() => periodFromParams(searchParams), [searchParams])

  // Mount: defaults + execução ativa + histórico. Unmount: o polling para.
  useEffect(() => {
    clearError()
    void loadDefaults()
    void loadHistory({ page: 1 })

    return () => {
      pausePolling()
    }
  }, [clearError, loadDefaults, loadHistory, pausePolling])

  const activeRun = currentRun !== null && isImportRunActive(currentRun.status) ? currentRun : null
  const finishedRun = lastRun !== null && !isImportRunActive(lastRun.status) ? lastRun : null

  // Uma execução que acabou de terminar entra no histórico sem recarregar a tela.
  const lastFinishedId = useRef<number | null>(null)

  useEffect(() => {
    if (finishedRun === null || lastFinishedId.current === finishedRun.id) return

    lastFinishedId.current = finishedRun.id
    void loadHistory()
  }, [finishedRun, loadHistory])

  /** Sucesso dos dois inícios: o toast, o histórico na primeira página. */
  const onStarted = useCallback((): void => {
    toast.success(t('import.startedToast'))
    void loadHistory({ page: 1 })
  }, [loadHistory, t])

  const handleSubmit = useCallback(
    (values: ImportFormValues): void => {
      void startRun({
        startDate: values.startDate,
        endDate: values.endDate,
        dryRun: values.dryRun,
      }).then((created) => {
        if (created !== null) onStarted()
      })
    },
    [onStarted, startRun],
  )

  /**
   * "Atualizar base" (FR-013): o período NÃO sai daqui — o corpo é só o modo, e o
   * servidor recusa sem credenciais, sem pedido importado ou com intervalo acima do
   * limite (400), com a mensagem dele na tela.
   */
  const handleUpdateBase = useCallback(
    (dryRun: boolean): void => {
      void startUpdateBase({ dryRun }).then((created) => {
        if (created !== null) onStarted()
      })
    },
    [onStarted, startUpdateBase],
  )

  const handleCancel = useCallback((): void => {
    if (activeRun === null) return

    void cancelRun(activeRun.id).then(() => {
      toast.info(t('import.cancelToast'))
    })
  }, [activeRun, cancelRun, t])

  const handleOpen = useCallback(
    (runId: number): void => {
      void loadRun(runId)
    },
    [loadRun],
  )

  const handlePageChange = useCallback(
    (page: number): void => {
      void loadHistory({ page })
    },
    [loadHistory],
  )

  return (
    // Dentro do `AppShell`, que já é o <main>: aqui é só a coluna da tela.
    <div className="mx-auto flex w-full max-w-screen-md flex-col gap-4">
      <h1 className="text-3xl font-bold tracking-tight text-foreground">{t('import.title')}</h1>

      {defaults === null && loading ? <LoadingList count={2} /> : null}

      {activeRun !== null ? (
        <ImportProgress
          run={activeRun}
          busy={starting}
          onCancel={handleCancel}
          onRefresh={() => {
            void refreshCurrent()
          }}
        />
      ) : (
        <ImportForm
          defaults={defaults}
          initialPeriod={initialPeriod}
          busy={starting}
          hasActiveRun={false}
          serverError={error}
          onSubmit={handleSubmit}
          onUpdateBase={handleUpdateBase}
        />
      )}

      {finishedRun !== null ? <ImportReport run={finishedRun} /> : null}

      <ImportHistory
        history={history}
        selectedRunId={finishedRun?.id ?? null}
        onOpen={handleOpen}
        onPageChange={handlePageChange}
      />
    </div>
  )
}

export default ImportPage
