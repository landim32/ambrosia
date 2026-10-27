import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Search } from 'lucide-react'

import { CustomerCard } from '@/components/customers/CustomerCard'
import { EmptyState } from '@/components/common/EmptyState'
import { ErrorState } from '@/components/common/ErrorState'
import { LoadingList } from '@/components/common/LoadingList'
import { Pagination } from '@/components/common/Pagination'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useCustomer } from '@/hooks/useCustomer'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { parsePage } from '@/lib/filters'
import { PAGE_SIZE } from '@/types/common'

/**
 * Lista de clientes (US2, FR-008/FR-009/FR-025).
 *
 * A URL manda: `?busca=` e `?pagina=` são o estado da consulta, e a busca entra
 * em vigor sozinha depois da pausa de digitação (400 ms), sem botão (FR-009).
 * Mudar a busca reposiciona a página 1 com `replace` (não entope o histórico com
 * cada tecla); mudar de página usa `push` (voltar do navegador percorre páginas),
 * conforme `contracts/ui-routes.md`.
 *
 * O `writtenBusca` é o que separa "a vendedora digitou" de "a URL mudou por
 * fora". Sem ele, o valor estável antigo — que chega 400 ms depois — re-gravava
 * na URL o termo que o botão "Limpar busca" ou o botão voltar acabou de apagar
 * (bug encontrado na US2).
 */
export const CustomersPage = () => {
  const { t } = useTranslation()
  const [searchParams, setSearchParams] = useSearchParams()
  const { customers, totalCount, loading, error, loadCustomers, retryCustomers } = useCustomer()

  const urlBusca = searchParams.get('busca') ?? ''
  const page = parsePage(searchParams)

  const [draft, setDraft] = useState(urlBusca)
  const debouncedDraft = useDebouncedValue(draft)
  const writtenBusca = useRef(urlBusca)

  // URL mudou por fora (voltar, "Limpar busca", endereço colado): o rascunho
  // segue, e a escrita fica quieta porque já sabe desse valor.
  useEffect(() => {
    if (urlBusca === writtenBusca.current) return

    writtenBusca.current = urlBusca
    setDraft(urlBusca)
  }, [urlBusca])

  // Digitação estável NOVA → grava na URL com página 1.
  useEffect(() => {
    const term = debouncedDraft.trim()
    if (term === writtenBusca.current) return

    writtenBusca.current = term

    const next = new URLSearchParams(searchParams)
    if (term === '') {
      next.delete('busca')
    } else {
      next.set('busca', term)
    }
    next.delete('pagina')
    setSearchParams(next, { replace: true })
  }, [debouncedDraft, searchParams, setSearchParams])

  useEffect(() => {
    void loadCustomers({ search: urlBusca, page, pageSize: PAGE_SIZE })
  }, [loadCustomers, urlBusca, page])

  const changePage = useCallback(
    (target: number): void => {
      const next = new URLSearchParams(searchParams)
      next.set('pagina', String(target))
      setSearchParams(next)
    },
    [searchParams, setSearchParams],
  )

  const clearSearch = useCallback((): void => {
    const next = new URLSearchParams(searchParams)
    next.delete('busca')
    next.delete('pagina')
    setSearchParams(next, { replace: true })
  }, [searchParams, setSearchParams])

  const backToFirstPage = useCallback((): void => {
    const next = new URLSearchParams(searchParams)
    next.delete('pagina')
    setSearchParams(next, { replace: true })
  }, [searchParams, setSearchParams])

  const isEmpty = !loading && error === null && customers.length === 0
  const beyondEnd = isEmpty && totalCount > 0

  const emptyAction = useMemo(
    () =>
      beyondEnd
        ? { label: t('common.backToFirstPage'), onAction: backToFirstPage }
        : { label: t('common.clearSearch'), onAction: clearSearch },
    [beyondEnd, backToFirstPage, clearSearch, t],
  )

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-3xl font-bold tracking-tight text-foreground">{t('customers.title')}</h1>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="customer-search" className="text-xs font-normal text-muted-foreground">
          {t('customers.searchLabel')}
        </Label>
        <div className="relative">
          <Search
            aria-hidden="true"
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
          />
          <Input
            id="customer-search"
            className="min-h-11 rounded-xl border-transparent bg-secondary pl-9 dark:bg-secondary"
            type="search"
            name="busca"
            autoComplete="off"
            value={draft}
            onChange={(event) => {
              setDraft(event.target.value)
            }}
          />
        </div>
      </div>

      {loading ? <LoadingList count={4} /> : null}

      {/* FR-025/FR-026 (T086): "Tentar de novo" repete a última consulta, que o
          próprio contexto guardou — a página não remonta nem refaz a carga. */}
      {!loading && error !== null ? <ErrorState message={error} onRetry={retryCustomers} /> : null}

      {isEmpty ? (
        <EmptyState
          title={t('customers.empty')}
          actionLabel={emptyAction.label}
          onAction={emptyAction.onAction}
        />
      ) : null}

      {!loading && customers.length > 0 ? (
        <>
          {/* Lista agrupada do iOS: um bloco arredondado com separadores finos. */}
          <ul
            role="list"
            className="divide-y divide-border/70 overflow-hidden rounded-2xl border border-border/70 bg-card"
          >
            {customers.map((customer) => (
              <li key={customer.id} className="min-w-0">
                <CustomerCard customer={customer} />
              </li>
            ))}
          </ul>

          {/* O contador de resultados com `aria-live` mora no Pagination (FR-029). */}
          <Pagination
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={changePage}
          />
        </>
      ) : null}
    </div>
  )
}

export default CustomersPage
