import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { initials } from '@/lib/initials'

export interface CustomerAvatarProps {
  name: string
  /**
   * Reservado para a HU 010: enquanto a API da 001 não devolve o campo, chega
   * `undefined` e o avatar usa as iniciais (research §20).
   */
  photoUrl?: string | null
  size?: 'default' | 'lg'
}

/**
 * Avatar do cliente (FR-010): foto quando houver, iniciais quando não.
 *
 * `referrerPolicy="no-referrer"` na imagem: a URL do +Vendas não pode receber a
 * origem do Ambrosia (FR-023, mesma regra das imagens dos itens). O fallback é
 * `aria-hidden` porque o nome já é lido ao lado, em cada card — o leitor de tela
 * não anuncia "MS Maria Silva".
 */
export const CustomerAvatar = ({ name, photoUrl, size = 'default' }: CustomerAvatarProps) => (
  <Avatar size={size} className="shrink-0 border bg-muted">
    {photoUrl ? (
      <AvatarImage src={photoUrl} alt="" referrerPolicy="no-referrer" />
    ) : null}
    <AvatarFallback aria-hidden="true">{initials(name)}</AvatarFallback>
  </Avatar>
)

export default CustomerAvatar
