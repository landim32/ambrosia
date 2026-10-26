import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

import '@/i18n'

/**
 * jsdom não implementa `scrollIntoView`, e o `Select` do Radix (via shadcn/ui) o
 * chama no efeito de abertura do popover: sem o stub, qualquer teste que abra a
 * lista "Tipo" morre com TypeError antes da asserção. Define sempre — o stub é
 * do ambiente de teste e não muda nada em produção.
 */
Object.defineProperty(window.Element.prototype, 'scrollIntoView', {
  writable: true,
  configurable: true,
  value: () => undefined,
})

/**
 * Ambiente de teste: jsdom + jest-dom + i18n já inicializado (`initImmediate:false`).
 * `localStorage` é limpo entre testes — a sessão nunca vaza de um teste para o outro.
 */
afterEach(() => {
  cleanup()
  localStorage.clear()
})
