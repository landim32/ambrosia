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
 * Tela `/importacao` (feature 006, US1/US2/US3/US4/US7).
 *
 * Fluxo: ao montar, busca os defaults (datas sugeridas e bloqueio) e retoma a
 * execução ativa, se houver — fechar e reabrir a aba volta ao progresso (US7-1),
 * porque a execução vive no servidor. Com execução ativa a tela mostra o
 * progresso no lugar do formulário (um segundo início seria 409, FR-016); ao
 * terminar, mostra o relatório e atualiza o histórico.
 *
 * `?inicio=AAAA-MM-DD&fim=AAAA-MM-DD` é o destino de "Repetir este bloco"
 * (FR-026): pré-preenche o período. Login e senha nunca vêm por URL (FR-007).
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

  const handleSubmit = useCallback(
    (values: ImportFormValues): void => {
      // O objeto com a senha é montado aqui, entregue ao serviço e descartado: não
      // vai para estado global, storage ou log (FR-005/FR-007).
      void startRun({
        login: values.login.trim(),
        password: values.password,
        startDate: values.startDate,
        endDate: values.endDate,
        dryRun: values.dryRun,
      }).then((created) => {
        if (created !== null) {
          toast.success(t('import.startedToast'))
          void loadHistory({ page: 1 })
        }
      })
    },
    [loadHistory, startRun, t],
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
    <main className="mx-auto flex w-full max-w-screen-md flex-col gap-4 px-4 py-6">
      <h1 className="text-xl font-semibold text-foreground">{t('import.title')}</h1>

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
        />
      )}

      {finishedRun !== null ? <ImportReport run={finishedRun} /> : null}

      <ImportHistory
        history={history}
        selectedRunId={finishedRun?.id ?? null}
        onOpen={handleOpen}
        onPageChange={handlePageChange}
      />
    </main>
  )
}

export default ImportPage
