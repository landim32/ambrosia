import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'

import type { OrderFilters } from '@/types/order'

/** Valor do `Select` para "todos": o Radix não aceita string vazia. */
const ALL = 'all'

/** Limite de `channel`/`search` na API (`OrderListQueryValidator`: 60). */
const MAX_TEXT = 60

export interface OrderFiltersBarProps {
  /** Filtros correntes (a URL, via `useOrderFilters`) — a barra é controlada. */
  filters: OrderFilters
  /** Parcial + `page` já resolvido; quem navega é a página (research §9). */
  onChange: (patch: Partial<OrderFilters>) => void
  /** "Limpar filtros" — a página sabe o que zerar (inclusive `pagina`). */
  onClear: () => void
}

/**
 * Campo de texto controlado pela URL, com debounce de 400 ms (research §14): uma
 * tecla não pode virar uma requisição.
 *
 * `onStable` só dispara quando o valor estabilizado veio da DIGITAÇÃO. Isso é o
 * que faltava para o botão "Limpar filtros" funcionar: ao limpar, a URL vai a
 * vazio, mas `useDebouncedValue` ainda entrega o termo antigo por até 400 ms —
 * sem o gate, uma escrita "estável" regravaria na URL o filtro que acabou de
 * sair dela (o mesmo bug que a busca de `/clientes` tinha).
 *
 * `seen` guarda o último valor EXTERNO visto: é ele que distingue "a URL mudou"
 * de "a vendedora digitou" — comparar o rascunho com o valor externo cancelaria a
 * própria digitação no instante da primeira tecla.
 */
const useUrlDrivenField = (
  externalValue: string,
  onStable: (value: string) => void,
): { draft: string; change: (value: string) => void } => {
  const [draft, setDraft] = useState(externalValue)
  const typed = useRef(false)
  const seen = useRef(externalValue)
  const debounced = useDebouncedValue(draft)
  const stable = useRef(onStable)

  useEffect(() => {
    stable.current = onStable
  }, [onStable])

  // A URL mudou por fora: o rascunho acompanha e a escrita fica desarmada.
  useEffect(() => {
    if (seen.current === externalValue) return

    seen.current = externalValue
    typed.current = false
    setDraft(externalValue)
  }, [externalValue])

  useEffect(() => {
    if (!typed.current || draft !== debounced) return

    typed.current = false
    stable.current(debounced.trim())
  }, [draft, debounced])

  const change = useCallback((value: string): void => {
    typed.current = true
    setDraft(value)
  }, [])

  return { draft, change }
}

/**
 * Barra de filtros de `/pedidos` (FR-014/FR-016, T072).
 *
 * - Período em `<input type="date">`: o `AAAA-MM-DD` vai COMO DIGITADO, sem
 *   conversão de fuso (research §10) — o dia-calendário escolhido é o dia em
 *   Brasília, inclusive com o navegador em outro fuso (SC-007).
 * - Canal é texto livre com `maxLength=60`, o limite da API: sem lista
 *   pré-definida (FR-014) e sem seletor de "mês de cota" (FR-016).
 * - Canal e busca têm debounce; tipo e datas são imediatos — escolha, não
 *   digitação. Repor `page: 1` é papel do `useOrderFilters`, não daqui.
 * - Layout: coluna em 360 px, grade acima de `sm` (FR-028); todo controle tem
 *   `Label` associado (FR-029).
 */
export const OrderFiltersBar = ({ filters, onChange, onClear }: OrderFiltersBarProps) => {
  const { t } = useTranslation()

  const channel = useUrlDrivenField(filters.channel ?? '', (value) => {
    onChange({ channel: value === '' ? undefined : value, page: 1 })
  })

  const search = useUrlDrivenField(filters.search ?? '', (value) => {
    onChange({ search: value === '' ? undefined : value, page: 1 })
  })

  return (
    <section
      aria-label={t('orders.filtersAriaLabel')}
      className="flex flex-col gap-3 rounded-lg border bg-card p-4"
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="filter-de">{t('orders.dateFrom')}</Label>
          <Input
            id="filter-de"
            className="min-h-11"
            name="de"
            type="date"
            max={filters.to ?? undefined}
            value={filters.from ?? ''}
            onChange={(event) => {
              onChange({
                from: event.target.value === '' ? undefined : event.target.value,
                page: 1,
              })
            }}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="filter-ate">{t('orders.dateTo')}</Label>
          <Input
            id="filter-ate"
            className="min-h-11"
            name="ate"
            type="date"
            min={filters.from ?? undefined}
            value={filters.to ?? ''}
            onChange={(event) => {
              onChange({
                to: event.target.value === '' ? undefined : event.target.value,
                page: 1,
              })
            }}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="filter-tipo">{t('orders.kindLabel')}</Label>
          <Select
            value={filters.kind ?? ALL}
            onValueChange={(value) => {
              onChange({
                kind: value === ALL ? undefined : (value as OrderFilters['kind']),
                page: 1,
              })
            }}
          >
            <SelectTrigger id="filter-tipo" className="min-h-11 w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>{t('orders.all')}</SelectItem>
              <SelectItem value="sale">{t('orders.sale')}</SelectItem>
              <SelectItem value="return">{t('orders.return')}</SelectItem>
              <SelectItem value="cancellation">{t('orders.cancellation')}</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="filter-canal">{t('orders.channelLabel')}</Label>
          <Input
            id="filter-canal"
            className="min-h-11"
            name="canal"
            type="text"
            autoComplete="off"
            maxLength={MAX_TEXT}
            value={channel.draft}
            onChange={(event) => {
              channel.change(event.target.value)
            }}
          />
        </div>

        <div className="flex flex-col gap-1.5 sm:col-span-2">
          <Label htmlFor="filter-busca">{t('orders.searchLabel')}</Label>
          <Input
            id="filter-busca"
            className="min-h-11"
            name="busca"
            type="search"
            autoComplete="off"
            maxLength={MAX_TEXT}
            value={search.draft}
            onChange={(event) => {
              search.change(event.target.value)
            }}
          />
        </div>
      </div>

      <Button
        type="button"
        variant="outline"
        className="min-h-11 self-start sm:self-auto"
        onClick={onClear}
      >
        {t('orders.clearFilters')}
      </Button>
    </section>
  )
}

export default OrderFiltersBar
