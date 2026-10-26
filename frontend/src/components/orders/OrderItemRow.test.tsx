import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { OrderItemRow } from '@/components/orders/OrderItemRow'

import type { OrderItemInfo } from '@/types/order'

/**
 * Linha do item do pedido (FR-017, T075/T079): imagem, nome, referência (SKU),
 * tamanho e preço em reais. Valor `null` da API vira o traço de "indisponível" —
 * nunca a palavra "null", nunca um buraco que desalinhe a linha.
 */
const nb = (value: string): string => value.replace(/\u00a0/g, ' ').replace(/\u202f/g, ' ')

const item = (overrides: Partial<OrderItemInfo> = {}): OrderItemInfo => ({
  id: 1,
  lineNo: 1,
  productName: 'Vestido Longo de Linho',
  skuRef: 'VL-100',
  size: 'M',
  price: 450,
  imageUrl: 'https://example.org/vestido.jpg',
  ...overrides,
})

/** O valor de um atributo, pela testid estável (o rótulo é a própria chave). */
const attribute = (label: string): string => screen.getByTestId(`item-${label}`).textContent

const rowText = (): string => screen.getByTestId('order-item-row').textContent

describe('OrderItemRow', () => {
  it('mostra nome, referência, tamanho e preço', () => {
    render(<OrderItemRow item={item()} />)

    expect(screen.getByText('Vestido Longo de Linho')).toBeInTheDocument()
    expect(attribute('Referência')).toBe('VL-100')
    expect(attribute('Tamanho')).toBe('M')
    expect(screen.getByText(nb('R$ 450,00'))).toBeInTheDocument()
  })

  it('a imagem usa o nome do produto como texto alternativo (FR-023)', () => {
    render(<OrderItemRow item={item()} />)

    expect(screen.getByRole('img', { name: 'Vestido Longo de Linho' })).toHaveAttribute(
      'src',
      'https://example.org/vestido.jpg',
    )
  })

  it('item sem imagem mostra o placeholder no lugar, sem quebrar', () => {
    render(<OrderItemRow item={item({ imageUrl: null })} />)

    expect(screen.getByTestId('image-placeholder')).toBeInTheDocument()
  })

  it('valores nulos saem como traço, não como texto cru', () => {
    render(
      <OrderItemRow
        item={item({ productName: null, skuRef: null, size: null, price: null, imageUrl: null })}
      />
    )

    expect(screen.queryByText('null')).not.toBeInTheDocument()
    expect(screen.queryByText('undefined')).not.toBeInTheDocument()
    expect(attribute('Referência')).toBe('—')
    expect(attribute('Tamanho')).toBe('—')
    expect(rowText()).toContain('—')
  })

  /** Edge Case: a API aceita o item com nome OU SKU (OrderItemInsertInfoValidator). */
  it('item sem nome mas com referência continua legível', () => {
    render(<OrderItemRow item={item({ productName: null, skuRef: 'SEM-NOME-01' })} />)

    // O SKU faz o papel do nome: título da linha, valor do atributo e alt.
    expect(attribute('Referência')).toBe('SEM-NOME-01')
    expect(screen.getAllByText('SEM-NOME-01').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByRole('img', { name: 'SEM-NOME-01' })).toBeInTheDocument()
  })

  it('o preço aceita centavos (FR-019)', () => {
    render(<OrderItemRow item={item({ price: 89.9 })} />)

    expect(screen.getByText(nb('R$ 89,90'))).toBeInTheDocument()
  })

  it('a linha não imprime o nome do campo da API', () => {
    render(<OrderItemRow item={item({ lineNo: 3 })} />)

    expect(rowText()).not.toContain('lineNo')
    expect(rowText()).not.toContain('skuRef')
  })
})
