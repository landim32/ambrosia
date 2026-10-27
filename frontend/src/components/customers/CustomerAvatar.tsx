import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { initials } from '@/lib/initials'
import { cn } from '@/lib/utils'

export interface CustomerAvatarProps {
  name: string
  /**
   * Reservado para a HU 010: enquanto a API da 001 não devolve o campo, chega
   * `undefined` e o avatar usa as iniciais (research §20).
   */
  photoUrl?: string | null
  /** `xl` é o avatar do perfil (96 px no mobile, 128 px no desktop). */
  size?: 'default' | 'lg' | 'xl'
  className?: string
}

const sizeClass: Record<NonNullable<CustomerAvatarProps['size']>, string> = {
  default: 'size-11',
  lg: 'size-14',
  xl: 'size-24 md:size-32',
}

const textClass: Record<NonNullable<CustomerAvatarProps['size']>, string> = {
  default: 'text-sm',
  lg: 'text-lg',
  xl: 'text-3xl md:text-4xl',
}

/**
 * Avatar do cliente (FR-010): foto quando houver, iniciais quando não — as
 * iniciais em branco sobre o gradiente azul da marca.
 *
 * `referrerPolicy="no-referrer"` na imagem: a URL do +Vendas não pode receber a
 * origem do Ambrosio (FR-023, mesma regra das imagens dos itens). O fallback é
 * `aria-hidden` porque o nome já é lido ao lado, em cada card — o leitor de tela
 * não anuncia "MS Maria Silva".
 */
export const CustomerAvatar = ({ name, photoUrl, size = 'default', className }: CustomerAvatarProps) => (
  <Avatar className={cn('shrink-0', sizeClass[size], className)}>
    {photoUrl ? (
      <AvatarImage src={photoUrl} alt="" referrerPolicy="no-referrer" className="object-cover" />
    ) : null}
    <AvatarFallback
      aria-hidden="true"
      className={cn(
        'bg-gradient-to-br from-brand to-brand-navy font-semibold tracking-wide text-white',
        textClass[size],
      )}
    >
      {initials(name)}
    </AvatarFallback>
  </Avatar>
)

export default CustomerAvatar
