import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { LazyImage } from '@/components/common/LazyImage'

/**
 * LazyImage (FR-023, T075): carrega sob demanda, não vaza a origem do Ambrosio
 * ao servidor da imagem, e quando falta a URL (ou a imagem quebra) mostra o
 * placeholder no MESMO espaço — o layout da lista de itens não se move.
 */
describe('LazyImage', () => {
  it('renderiza a imagem com lazy, async e no-referrer', () => {
    render(<LazyImage src="https://example.org/vestido.jpg" alt="Vestido" />)

    const img = screen.getByRole('img', { name: 'Vestido' })
    expect(img).toHaveAttribute('src', 'https://example.org/vestido.jpg')
    expect(img).toHaveAttribute('loading', 'lazy')
    expect(img).toHaveAttribute('decoding', 'async')
    expect(img).toHaveAttribute('referrerpolicy', 'no-referrer')
  })

  it('sem src já nasce no placeholder, sem <img> quebrada', () => {
    const { container } = render(<LazyImage src={null} alt="Sem nome" />)

    // O placeholder tem role="img" (e nome proprio); o que nao pode existir e a
    // tag <img> apontando para nada.
    expect(container.querySelector('img')).toBeNull()
    expect(screen.getByTestId('image-placeholder')).toBeInTheDocument()
  })

  it('src vazio (string) conta como ausente', () => {
    const { container } = render(<LazyImage src="" alt="X" />)

    expect(container.querySelector('img')).toBeNull()
    expect(screen.getByTestId('image-placeholder')).toBeInTheDocument()
  })

  it('erro de carregamento troca pelo placeholder no mesmo espaço', async () => {
    const { container } = render(<LazyImage src="https://example.org/expirou.jpg" alt="Blusa" />)

    const img = screen.getByRole('img', { name: 'Blusa' })
    img.dispatchEvent(new Event('error'))

    await screen.findByTestId('image-placeholder')
    expect(container.querySelector('img')).toBeNull()
  })

  it('placeholder é anunciado como imagem, com rótulo próprio (FR-029)', () => {
    render(<LazyImage src={null} alt="Blusa" />)

    const placeholder = screen.getByTestId('image-placeholder')
    expect(placeholder).toHaveAttribute('role', 'img')
    expect(placeholder).toHaveAccessibleName('Sem imagem')
  })

  it('o contêiner reserva o quadrado, com ou sem imagem (FR-023)', () => {
    const { rerender } = render(<LazyImage src="https://example.org/a.jpg" alt="A" />)
    const withImage = screen.getByRole('img', { name: 'A' }).parentElement

    expect(withImage?.className ?? '').toContain('aspect-square')

    rerender(<LazyImage src={null} alt="A" />)
    const placeholder = screen.getByTestId('image-placeholder').parentElement
    expect(placeholder?.className ?? '').toContain('aspect-square')
  })

  it('um novo src volta a tentar a imagem (lista que repagina)', () => {
    const { rerender } = render(<LazyImage src="https://example.org/a.jpg" alt="A" />)
    screen.getByRole('img', { name: 'A' }).dispatchEvent(new Event('error'))

    rerender(<LazyImage src="https://example.org/b.jpg" alt="A" />)

    expect(screen.getByRole('img', { name: 'A' })).toHaveAttribute('src', 'https://example.org/b.jpg')
  })
})
