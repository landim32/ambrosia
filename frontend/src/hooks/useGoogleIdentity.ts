import { useEffect, useRef } from 'react'

import type { RefObject } from 'react'

const GSI_SRC = 'https://accounts.google.com/gsi/client'

/** Assinaturas do `google.accounts.id` que usamos (mesmo namespace dos tipos). */
type GoogleIdApi = typeof google.accounts.id

/** Forma real do global antes/depois de o script do GSI carregar. */
interface GoogleGlobal {
  readonly accounts?: {
    readonly id?: GoogleIdApi
  }
}

/**
 * Leitura tipada do global injetado pelo GSI (research §2, T040).
 *
 * `@types/google.accounts` declara o namespace `google` como SEMPRE presente e
 * `window` vale `Window & typeof globalThis`, então qualquer augmentation em
 * `Window` vira uma interseção não-nula: para o TypeScript (e para o ESLint) a
 * ausência passaria a ser impossível, e as guardas de "o script ainda não
 * carregou" seriam chamadas de código morto. Este accessor modela a ausência
 * real com um único cast na fronteira do runtime — sem `any` (constituição §IV).
 */
const readGoogleIdApi = (): GoogleIdApi | undefined =>
  (window as unknown as { readonly google?: GoogleGlobal }).google?.accounts?.id

/**
 * Injeção única do script oficial do Google Identity Services: uma promise de
 * módulo garante que a `<script>` entre no documento uma só vez, mesmo com o
 * React StrictMode montando os efeitos duas vezes em desenvolvimento.
 */
let gsiLoader: Promise<void> | null = null

const loadGoogleIdentity = (): Promise<void> => {
  if (typeof window === 'undefined') return Promise.reject(new Error('DOM indisponível'))
  if (readGoogleIdApi() !== undefined) return Promise.resolve()
  if (gsiLoader) return gsiLoader

  gsiLoader = new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = GSI_SRC
    script.async = true
    script.defer = true
    script.onload = () => {
      resolve()
    }
    script.onerror = () => {
      gsiLoader = null
      reject(new Error('Google Identity Services não carregou'))
    }
    document.head.appendChild(script)
  })

  return gsiLoader
}

/**
 * Estica o botão oficial (no máximo 400×40) até cobrir o contêiner inteiro, para
 * que nenhuma borda do botão visível do site fique sem área de toque. A escala é
 * aplicada depois que o GIS termina de desenhar o botão.
 */
const coverContainer = (container: HTMLDivElement): void => {
  requestAnimationFrame(() => {
    const button = container.firstElementChild
    if (!(button instanceof HTMLElement)) return

    const target = container.getBoundingClientRect()
    const drawn = button.getBoundingClientRect()
    if (drawn.width === 0 || drawn.height === 0) return

    button.style.transformOrigin = 'center'
    button.style.transform = `scale(${String(target.width / drawn.width)}, ${String(target.height / drawn.height)})`
  })
}

/**
 * useGoogleIdentity - botão "Entrar com Google" sem dependência de terceiros.
 *
 * Recebe o callback que recebe o `credential` (o ID token que vai a
 * `POST /auth/google`) e devolve a ref do contêiner onde o GIS desenha o botão.
 * Com `VITE_GOOGLE_CLIENT_ID` vazio não injeta script nem renderiza nada
 * (research §2), deixando a tela apenas com as outras formas de entrada.
 */
export const useGoogleIdentity = (
  onCredential: (credential: string) => void,
): RefObject<HTMLDivElement> => {
  const clientId = import.meta.env.VITE_GOOGLE_CLIENT_ID
  const containerRef = useRef<HTMLDivElement>(null)
  const callbackRef = useRef(onCredential)

  useEffect(() => {
    callbackRef.current = onCredential
  }, [onCredential])

  useEffect(() => {
    if (clientId === undefined || clientId.trim() === '') return

    let cancelled = false

    void loadGoogleIdentity()
      .then(() => {
        const identity = readGoogleIdApi()
        if (cancelled || identity === undefined || containerRef.current === null) return

        identity.initialize({
          client_id: clientId,
          callback: (response) => {
            if (response.credential !== '') {
              callbackRef.current(response.credential)
            }
          },
        })
        // O botão oficial fica transparente sobre o botão do site
        // (`GoogleSignInButton`): ocupa a largura do contêiner, até o máximo de
        // 400px que o GIS aceita, para a área de toque cobrir o botão visível.
        identity.renderButton(containerRef.current, {
          type: 'standard',
          theme: 'filled_black',
          size: 'large',
          shape: 'rectangular',
          locale: 'pt-BR',
          text: 'signin_with',
          width: Math.min(400, Math.max(200, containerRef.current.offsetWidth || 320)),
        })
        coverContainer(containerRef.current)
      })
      .catch(() => {
        /* Sem rede para o GIS: a tela segue com as outras formas de entrada. */
      })

    return () => {
      cancelled = true
    }
  }, [clientId])

  return containerRef
}

export default useGoogleIdentity
