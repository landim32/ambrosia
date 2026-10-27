import { apiRequest } from '@/Services/apiClient'

import type { GoogleLoginInfo, LoginInfo, LoginResult, RegisterInfo } from '@/types/auth'

/**
 * Auth Service - troca da credencial pelo JWT da API (HU 001 + 003).
 *
 * O transporte é sempre `apiRequest` (Fetch API, constituição §II): as rotas
 * `/auth/*` são anônimas (`auth: false`) e os erros chegam como classes,
 * conforme `contracts/api-consumption.md`. Essa é a exceção formal à regra 7 da
 * skill `react-architecture`, registrada no Complexity Tracking do plano.
 */
class AuthService {
  /**
   * `POST /auth/register` (FR-001): cria a conta e devolve o `LoginResult` — a
   * sessão começa no mesmo passo, sem um segundo login.
   *
   * Erros que a tela precisa distinguir (contracts/auth-api.md): 400 chega com
   * `erros[]` por campo, 409 com a `mensagem` geral (e-mail já cadastrado).
   * O `apiClient` já traduz ambos em `ApiError`; nada é reescrito aqui.
   */
  async register(info: RegisterInfo): Promise<LoginResult> {
    return apiRequest<LoginResult>('POST', '/auth/register', { body: info, auth: false })
  }

  /**
   * `POST /auth/login` com e-mail e senha (FR-002). O 401 de credencial recusada
   * vem como `ApiError(401, 'E-mail ou senha inválidos')` porque a chamada é
   * anônima — ele NÃO derruba sessão nem dispara o aviso de "Sua sessão expirou"
   * (research §3).
   */
  async loginWithPassword(info: LoginInfo): Promise<LoginResult> {
    return apiRequest<LoginResult>('POST', '/auth/login', { body: info, auth: false })
  }

  /**
   * `POST /auth/google` com o ID token do Google Identity Services.
   * 400 vira `ApiError` com a `mensagem` da API (mostrada em toast pela tela).
   */
  async loginWithGoogle(idToken: string): Promise<LoginResult> {
    const body: GoogleLoginInfo = { idToken }

    return apiRequest<LoginResult>('POST', '/auth/google', { body, auth: false })
  }
}

export const authService = new AuthService()
export default AuthService
