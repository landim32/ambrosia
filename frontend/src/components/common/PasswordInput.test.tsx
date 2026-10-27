import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { PasswordInput } from '@/components/common/PasswordInput'

const LABELS = { show: 'Mostrar senha', hide: 'Ocultar senha' }

/** `<input type="password">` não tem role de textbox: a busca é pelo `id` do campo. */
const inputOf = (id: string): HTMLInputElement => {
  const input = document.getElementById(id)

  if (!(input instanceof HTMLInputElement)) throw new Error(`campo ${id} não encontrado`)

  return input
}

/** Monta o campo dentro de um formulário para provar que o botão não submita. */
const renderInForm = (onChange = vi.fn()) => {
  const onSubmit = vi.fn()

  render(
    <form
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit()
      }}
    >
      <PasswordInput
        id="senha"
        name="password"
        value="senha-forte-1"
        onChange={onChange}
        autoComplete="current-password"
        toggleLabels={LABELS}
      />
      <button type="submit">Entrar</button>
    </form>,
  )

  return { onChange, onSubmit }
}

describe('PasswordInput (FR-020)', () => {
  it('começa como campo de senha com o botão "Mostrar senha"', () => {
    renderInForm()

    expect(inputOf('senha')).toHaveAttribute('type', 'password')
    expect(inputOf('senha')).toHaveAttribute('name', 'password')
    expect(inputOf('senha')).toHaveAttribute('autocomplete', 'current-password')
    expect(screen.getByRole('button', { name: LABELS.show })).toHaveAttribute('aria-pressed', 'false')
  })

  it('alterna para texto e volta, e o aria-pressed acompanha o estado', async () => {
    const events = userEvent.setup()
    renderInForm()

    await events.click(screen.getByRole('button', { name: LABELS.show }))

    expect(inputOf('senha')).toHaveAttribute('type', 'text')
    expect(screen.getByRole('button', { name: LABELS.hide })).toHaveAttribute('aria-pressed', 'true')

    await events.click(screen.getByRole('button', { name: LABELS.hide }))

    expect(inputOf('senha')).toHaveAttribute('type', 'password')
    expect(screen.getByRole('button', { name: LABELS.show })).toHaveAttribute('aria-pressed', 'false')
  })

  it('clique no botão NÃO envia o formulário', async () => {
    const events = userEvent.setup()
    const { onSubmit } = renderInForm()

    await events.click(screen.getByRole('button', { name: LABELS.show }))

    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('repassa onChange, autocomplete e os atributos de acessibilidade ao input', async () => {
    const events = userEvent.setup()
    const { onChange } = renderInForm()

    await events.type(inputOf('senha'), 'x')

    expect(onChange).toHaveBeenCalled()

    render(
      <PasswordInput
        id="confirmacao"
        name="confirmPassword"
        value=""
        onChange={vi.fn()}
        autoComplete="new-password"
        toggleLabels={LABELS}
        aria-describedby="confirmacao-erro"
        aria-invalid={true}
      />,
    )

    expect(inputOf('confirmacao')).toHaveAttribute('aria-describedby', 'confirmacao-erro')
    expect(inputOf('confirmacao')).toHaveAttribute('aria-invalid', 'true')
    expect(inputOf('confirmacao')).toHaveAttribute('autocomplete', 'new-password')
  })
})
