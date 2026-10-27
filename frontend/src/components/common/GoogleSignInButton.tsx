import { forwardRef } from 'react'
import { useTranslation } from 'react-i18next'

/** "G" do Google em traço único, na cor do texto do botão. */
const GoogleGlyph = () => (
  <svg viewBox="0 0 24 24" aria-hidden="true" className="size-5 fill-current">
    <path d="M21.35 11.1H12v2.98h5.35c-.23 1.4-1.66 4.1-5.35 4.1-3.22 0-5.85-2.67-5.85-5.96S8.78 6.26 12 6.26c1.83 0 3.06.78 3.76 1.45l2.56-2.47C16.68 3.7 14.55 2.75 12 2.75 6.9 2.75 2.75 6.9 2.75 12S6.9 21.25 12 21.25c5.34 0 8.88-3.75 8.88-9.04 0-.6-.07-1.07-.15-1.52z" />
  </svg>
)

/**
 * Botão "Entrar com Google" no padrão visual do Ambrosio.
 *
 * O Google Identity Services só desenha o próprio botão (num iframe, com temas
 * fixos), e é ele que devolve o ID token que a API confere. Por isso o botão
 * oficial continua aqui, renderizado pelo `useGoogleIdentity` no contêiner da
 * ref, mas transparente e por cima do botão visível: o toque e o teclado vão
 * para o iframe do Google, e o visual fica o do site. O hover e o foco do
 * iframe chegam ao visual pelo `group` do contêiner.
 */
export const GoogleSignInButton = forwardRef<HTMLDivElement>((_props, ref) => {
  const { t } = useTranslation()

  return (
    <div data-testid="google-button" className="group relative min-h-12 w-full">
      <div
        aria-hidden="true"
        className="pointer-events-none flex min-h-12 w-full items-center justify-center gap-2.5 rounded-xl bg-linear-to-r from-google-warm to-google-warm-deep text-[0.9375rem] font-semibold text-google-warm-foreground shadow-[0_8px_24px_-12px_var(--google-warm)] transition group-hover:brightness-110 group-active:brightness-95 group-focus-within:ring-[3px] group-focus-within:ring-google-warm/50"
      >
        <GoogleGlyph />
        {t('login.google')}
      </div>
      <div
        ref={ref}
        className="absolute inset-0 flex items-center justify-center overflow-hidden rounded-xl opacity-0"
      />
    </div>
  )
})

GoogleSignInButton.displayName = 'GoogleSignInButton'

export default GoogleSignInButton
