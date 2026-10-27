import { describe, expect, it } from 'vitest'

import { isValidDateOnly } from '@/lib/dates'
import { parseOrderFilters, parsePage, toSearchParams } from '@/lib/filters'

import type { OrderFilters } from '@/types/order'

/**
 * Parser URL <-> `OrderFilters` (data-model §4, T064).
 *
 * A URL fala portugues (`de`, `ate`, `tipo`, `canal`, `busca`, `pagina`) e a API
 * fala ingles (`from`, `to`, `kind`, `channel`, `search`, `page`) — o parser e a
 * unica ponte, e o lugar onde todo valor invalido morre em silencio (Edge Case
 * "Filtro invalido no endereco"): a tela carrega sem o filtro, nunca com ele
 * quebrado ou enviado a API (que responderia 400).
 */
const sp = (raw: string): URLSearchParams => new URLSearchParams(raw)

describe('parsePage', () => {
  it('padrao 1 quando ausente', () => {
    expect(parsePage(sp(''))).toBe(1)
  })

  it('inteiros >= 1', () => {
    expect(parsePage(sp('pagina=3'))).toBe(3)
  })

  it('espaco ao redor do numero nao muda a pagina', () => {
    expect(parsePage(sp('pagina=%203'))).toBe(3)
  })

  it('descarta zero, negativo, fracionario, texto e vazio', () => {
    for (const raw of ['pagina=0', 'pagina=-2', 'pagina=1.5', 'pagina=abc', 'pagina=', 'pagina=%20']) {
      expect(parsePage(sp(raw))).toBe(1)
    }
  })
})

describe('parseOrderFilters', () => {
  it('mapeia os nomes da URL para os nomes da API', () => {
    const filters = parseOrderFilters(
      sp('de=2026-01-05&ate=2026-02-01&tipo=return&canal=Linx&busca=4751&pagina=2'),
    )

    expect(filters).toEqual<OrderFilters>({
      from: '2026-01-05',
      to: '2026-02-01',
      kind: 'return',
      channel: 'Linx',
      search: '4751',
      page: 2,
      pageSize: 20,
    })
  })

  it('sem nada na URL: so pagina e tamanho de pagina', () => {
    expect(parseOrderFilters(sp(''))).toEqual<OrderFilters>({ page: 1, pageSize: 20 })
  })

  describe('periodo', () => {
    it('aceita AAAA-MM-DD valido', () => {
      expect(parseOrderFilters(sp('de=2026-03-01')).from).toBe('2026-03-01')
      expect(parseOrderFilters(sp('ate=2026-03-31')).to).toBe('2026-03-31')
    })

    it('descarta data invalida e mantem a outra', () => {
      expect(parseOrderFilters(sp('de=2026-13-99')).from).toBeUndefined()
      expect(parseOrderFilters(sp('ate=ontem')).to).toBeUndefined()
      expect(parseOrderFilters(sp('de=2026-02-30&ate=2026-03-01')).from).toBeUndefined()
      expect(parseOrderFilters(sp('de=2026-02-30&ate=2026-03-01')).to).toBe('2026-03-01')
    })

    /** Edge case: intervalo invertido nao tem leitura possivel — ignora ambos. */
    it('de > ate ignora os dois', () => {
      const filters = parseOrderFilters(sp('de=2026-05-01&ate=2026-04-01'))

      expect(filters.from).toBeUndefined()
      expect(filters.to).toBeUndefined()
    })

    it('de == ate e aceito (dia unico, inclusivo)', () => {
      const filters = parseOrderFilters(sp('de=2026-04-01&ate=2026-04-01'))

      expect(filters.from).toBe('2026-04-01')
      expect(filters.to).toBe('2026-04-01')
    })
  })

  describe('tipo', () => {
    it('aceita os tres tipos da API', () => {
      for (const kind of ['sale', 'return', 'cancellation'] as const) {
        expect(parseOrderFilters(sp(`tipo=${kind}`)).kind).toBe(kind)
      }
    })

    it('descarta tipo desconhecido, vazio e em outras grafias', () => {
      for (const raw of ['tipo=invalido', 'tipo=', 'tipo=SALE', 'tipo=devolucao', 'tipo=1']) {
        expect(parseOrderFilters(sp(raw)).kind).toBeUndefined()
      }
    })
  })

  describe('canal e busca', () => {
    it('canal faz trim e aceita ate 60', () => {
      const sixty = 'a'.repeat(60)
      expect(parseOrderFilters(sp(`canal=%20${sixty}%20`)).channel).toBe(sixty)
    })

    /** Acima do que a API aceita (MaximumLength 60) descarta: 400 nao e opcao. */
    it('canal acima de 60 descarta', () => {
      expect(parseOrderFilters(sp(`canal=${'a'.repeat(61)}`)).channel).toBeUndefined()
    })

    it('canal ou busca so espacos vira ausente', () => {
      expect(parseOrderFilters(sp('canal=%20%20')).channel).toBeUndefined()
      expect(parseOrderFilters(sp('busca=%20')).search).toBeUndefined()
    })

    it('busca faz trim e aceita ate 60', () => {
      expect(parseOrderFilters(sp('busca=%204751%20')).search).toBe('4751')
      expect(parseOrderFilters(sp(`busca=${'b'.repeat(60)}`)).search).toBe('b'.repeat(60))
    })

    it('busca acima de 60 descarta', () => {
      expect(parseOrderFilters(sp(`busca=${'b'.repeat(61)}`)).search).toBeUndefined()
    })
  })

  it('parametros que nao sao da tela passam reto (customerId do perfil)', () => {
    const filters = parseOrderFilters(sp('pagina=2&utm_source=campanha'))

    expect(filters.page).toBe(2)
    expect(filters.customerId).toBeUndefined()
  })

  it('customerId numerico quando presente na URL', () => {
    expect(parseOrderFilters(sp('cliente=12')).customerId).toBe(12)
    expect(parseOrderFilters(sp('cliente=abc')).customerId).toBeUndefined()
  })

  it('a saida do parser nunca produz data invalida para a API', () => {
    for (const raw of ['de=2026-13-99', 'de=20260101', 'de=2026-1-1', 'ate=2026-02-29']) {
      const filters = parseOrderFilters(sp(raw))
      if (filters.from !== undefined) expect(isValidDateOnly(filters.from)).toBe(true)
      if (filters.to !== undefined) expect(isValidDateOnly(filters.to)).toBe(true)
    }
  })
})

describe('toSearchParams', () => {
  it('faz o caminho inverso do parser', () => {
    const raw = 'de=2026-01-05&ate=2026-02-01&tipo=cancellation&canal=Linx&busca=4751&pagina=3'

    // Comparacao canonica (ordenada): URLSearchParams reordena ao re-serializar
    // a string lida, e o que importa e o MESMO conjunto de pares (FR-015).
    const sorted = (params: URLSearchParams): string =>
      [...params.entries()].sort((a, b) => a[0].localeCompare(b[0])).map(([k, v]) => `${k}=${v}`).join('&')

    expect(sorted(toSearchParams(parseOrderFilters(sp(raw))))).toBe(sorted(sp(raw)))
  })

  it('omite vazios e a pagina 1', () => {
    expect(toSearchParams({ page: 1, pageSize: 20 }).toString()).toBe('')
    expect(toSearchParams({ page: 1, pageSize: 20, channel: '', search: '  ' }).toString()).toBe('')
  })

  it('escreve os nomes em portugues, na ordem do contrato', () => {
    const query = toSearchParams({
      page: 2,
      pageSize: 20,
      search: 'mar',
      kind: 'sale',
      to: '2026-02-01',
      from: '2026-01-05',
      channel: 'Site',
    })

    expect([...query.keys()]).toEqual(['de', 'ate', 'tipo', 'canal', 'busca', 'pagina'])
    expect([...query.values()]).toEqual(['2026-01-05', '2026-02-01', 'sale', 'Site', 'mar', '2'])
  })

  /** `customerId` e a consulta do perfil: vai na URL como `cliente`. */
  it('leva customerId como `cliente`', () => {
    expect(toSearchParams({ page: 1, pageSize: 20, customerId: 42 }).toString()).toBe('cliente=42')
  })

  it('round trip estavel para qualquer filtro valido', () => {
    const filters: OrderFilters = { from: '2026-01-01', kind: 'return', page: 4, pageSize: 20 }
    const twice = parseOrderFilters(toSearchParams(filters))

    expect(twice).toEqual(filters)
  })
})
