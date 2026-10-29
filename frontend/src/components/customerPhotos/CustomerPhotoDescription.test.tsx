import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { CustomerPhotoDescription } from '@/components/customerPhotos/CustomerPhotoDescription'

/**
 * Editor de descrição (T050, contracts/ui.md §4, FR-007): contador `n/500`,
 * Salvar/Cancelar, `Ctrl/Cmd+Enter`, "Adicionar descrição" quando vazia e o
 * limite travando o botão com aviso (US4-5).
 */
const renderEditor = (
  description: string | null,
  onSave = vi.fn().mockResolvedValue(true),
): typeof onSave => {
  render(<CustomerPhotoDescription description={description} onSave={onSave} />)

  return onSave
}

describe('CustomerPhotoDescription', () => {
  it('sem descrição oferece "Adicionar descrição" (US4-3)', () => {
    renderEditor(null)

    expect(screen.getByRole('button', { name: 'Adicionar descrição' })).toBeInTheDocument()
    expect(screen.queryByTestId('description-editor')).not.toBeInTheDocument()
  })

  it('com descrição mostra o texto e o botão Editar', async () => {
    const user = userEvent.setup()
    renderEditor('provando o vestido azul')

    expect(screen.getByText('provando o vestido azul')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Editar' }))

    expect(screen.getByTestId('description-editor')).toBeInTheDocument()
    expect(screen.getByTestId('description-textarea')).toHaveValue('provando o vestido azul')
  })

  it('mostra o contador n/500 enquanto digita', async () => {
    const user = userEvent.setup()
    renderEditor(null)

    await user.click(screen.getByRole('button', { name: 'Adicionar descrição' }))
    await user.type(screen.getByTestId('description-textarea'), 'saia verde')

    expect(screen.getByText('10/500')).toBeInTheDocument()
  })

  /** US4-4: texto só de espaços é "sem descrição" → `null`. */
  it('mandar vazio grava null', async () => {
    const user = userEvent.setup()
    const onSave = renderEditor('saia verde')

    await user.click(screen.getByRole('button', { name: 'Editar' }))
    await user.clear(screen.getByTestId('description-textarea'))
    await user.type(screen.getByTestId('description-textarea'), '   ')
    await user.click(screen.getByTestId('description-save'))

    expect(onSave).toHaveBeenCalledWith(null)
  })

  it('Ctrl+Enter salva', async () => {
    const user = userEvent.setup()
    const onSave = renderEditor(null)

    await user.click(screen.getByRole('button', { name: 'Adicionar descrição' }))
    await user.type(screen.getByTestId('description-textarea'), 'casamento')
    await user.keyboard('{Control>}{Enter/}')

    expect(onSave).toHaveBeenCalledWith('casamento')
  })

  /** US4-5: acima do limite nada é enviado, e a frase diz o máximo. */
  it('trava o Salvar acima de 500 caracteres', async () => {
    const user = userEvent.setup()
    const onSave = renderEditor(null)

    await user.click(screen.getByRole('button', { name: 'Adicionar descrição' }))
    await user.paste('x'.repeat(501))

    expect(screen.getByTestId('description-warning')).toHaveTextContent(
      'A descrição deve ter no máximo 500 caracteres.',
    )
    expect(screen.getByTestId('description-save')).toBeDisabled()

    await user.keyboard('{Control>}{Enter/}')
    expect(onSave).not.toHaveBeenCalled()
  })

  it('Cancelar fecha sem gravar e descarta o rascunho', async () => {
    const user = userEvent.setup()
    const onSave = renderEditor('saia verde')

    await user.click(screen.getByRole('button', { name: 'Editar' }))
    await user.clear(screen.getByTestId('description-textarea'))
    await user.type(screen.getByTestId('description-textarea'), 'outra coisa')
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(onSave).not.toHaveBeenCalled()
    expect(screen.queryByTestId('description-editor')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Editar' }))

    expect(screen.getByTestId('description-textarea')).toHaveValue('saia verde')
  })

  /** Recusa da API (400): o editor continua aberto com o texto na tela. */
  it('recusado, o editor fica aberto', async () => {
    const user = userEvent.setup()
    const onSave = vi.fn().mockResolvedValue(false)

    render(<CustomerPhotoDescription description={null} onSave={onSave} />)

    await user.click(screen.getByRole('button', { name: 'Adicionar descrição' }))
    await user.type(screen.getByTestId('description-textarea'), 'nota')
    await user.click(screen.getByTestId('description-save'))

    expect(onSave).toHaveBeenCalledWith('nota')
    expect(screen.getByTestId('description-editor')).toBeInTheDocument()
  })
})
