import { apiRequest } from '@/Services/apiClient'

import type { UserInfo } from '@/types/auth'

/**
 * User Service - perfil da vendedora autenticada.
 *
 * `GET /users/me` fica disponível para REVALIDAÇÃO da sessão; as telas usam o
 * `session.user` gravado no login (contracts/api-consumption.md), então este
 * método não é chamado a cada navegação.
 */
class UserService {
  /** Perfil da vendedora dono do token corrente. */
  async getMe(): Promise<UserInfo> {
    return apiRequest<UserInfo>('GET', '/users/me')
  }
}

export const userService = new UserService()
export default UserService
