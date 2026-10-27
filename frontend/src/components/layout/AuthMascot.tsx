import mascotLargeUrl from '@/assets/ambrosio-mascote.webp'
import mascotSmallUrl from '@/assets/ambrosio-mascote-sm.webp'
import { cn } from '@/lib/utils'

export interface AuthMascotProps {
  /**
   * - `compact`: acima do formulário no celular (~112 px de altura; 80 px em
   *   telas baixas como o iPhone SE, para o formulário caber em 375×667).
   * - `hero`: coluna própria ao lado do formulário no desktop (lg+).
   */
  variant: 'compact' | 'hero'
  className?: string
}

/** Proporção do recorte (530×840) para reservar o espaço antes do carregamento. */
const WIDTH = 530
const HEIGHT = 840

/**
 * Mascote do Ambrosio (robô branco com detalhes no azul da marca), recortado de
 * `docs/marketing/marcote.png` com fundo transparente.
 *
 * É DECORATIVO (`alt=""`): nas telas de entrada a logomarca e o título já
 * identificam a marca, e o leitor de tela não ganha nada com "robô sorrindo".
 *
 * Dois arquivos: 240 px de altura para o celular e 840 px para o desktop —
 * no máximo 2x o tamanho exibido. O `hero` é `loading="lazy"`: escondido no
 * mobile (`display: none`), o navegador nem chega a baixá-lo. A flutuação
 * suave (`animate-float`) desliga com `prefers-reduced-motion`.
 */
export const AuthMascot = ({ variant, className }: AuthMascotProps) => {
  if (variant === 'compact') {
    return (
      <img
        src={mascotSmallUrl}
        srcSet={`${mascotSmallUrl} 151w, ${mascotLargeUrl} ${String(WIDTH)}w`}
        sizes="72px"
        alt=""
        aria-hidden="true"
        width={WIDTH}
        height={HEIGHT}
        decoding="async"
        draggable={false}
        data-testid="auth-mascot"
        className={cn(
          'animate-float h-28 w-auto drop-shadow-[0_12px_24px_rgb(0_92_242/0.35)] select-none [@media(max-height:700px)]:h-20',
          className,
        )}
      />
    )
  }

  return (
    <div className={cn('relative isolate flex items-center justify-center', className)}>
      {/* Halo azul da marca atrás do mascote. */}
      <div
        aria-hidden="true"
        className="absolute inset-0 -z-10 m-auto size-[80%] rounded-full bg-brand/35 blur-3xl"
      />
      <img
        src={mascotLargeUrl}
        alt=""
        aria-hidden="true"
        width={WIDTH}
        height={HEIGHT}
        loading="lazy"
        decoding="async"
        draggable={false}
        data-testid="auth-mascot-hero"
        className="animate-float h-[26rem] w-auto drop-shadow-[0_24px_48px_rgb(0_92_242/0.35)] select-none xl:h-[30rem]"
      />
    </div>
  )
}

export default AuthMascot
