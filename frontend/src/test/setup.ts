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
 * jsdom não implementa `IntersectionObserver`, e a sentinela da rolagem infinita
 * da grade de fotos (feature 009, contracts/ui.md §2) constrói um no efeito: sem
 * o stub, qualquer teste que monte a grade morre com ReferenceError antes da
 * primeira asserção. O stub nunca intersecta — quem precisa da página seguinte
 * chama `loadMore` direto, como fazem os testes do contexto.
 */
class IntersectionObserverStub {
  readonly observe = (): void => undefined

  readonly unobserve = (): void => undefined

  readonly disconnect = (): void => undefined

  readonly takeRecords = (): IntersectionObserverEntry[] => []
}

Object.defineProperty(globalThis, 'IntersectionObserver', {
  value: IntersectionObserverStub,
  writable: true,
  configurable: true,
})

/**
 * Ambiente de teste: jsdom + jest-dom + i18n já inicializado (`initImmediate:false`).
 * `localStorage` é limpo entre testes — a sessão nunca vaza de um teste para o outro.
 */
afterEach(() => {
  cleanup()
  localStorage.clear()
})
