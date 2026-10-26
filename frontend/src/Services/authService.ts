import i18n from '@/i18n'
import { ApiError, apiRequest, NotFoundError } from '@/Services/apiClient'

import type { DevLoginInfo, GoogleLoginInfo, LoginResult } from '@/types/auth'

/**
 * Auth Service - troca da credencial do provedor pelo JWT da API (HU 001).
 *
 * O transporte é sempre `apiRequest` (Fetch API, constituição §II): as rotas
 * `/auth/*` são anônimas (`auth: false`) e os erros chegam como classes,
 * conforme `contracts/api-consumption.md`. Essa é a exceção formal à regra 7 da
 * skill `react-architecture`, registrada no Complexity Tracking do plano.
 */
class AuthService {
  /**
   * `POST /auth/google` com o ID token do Google Identity Services.
   * 400 vira `ApiError` com a `mensagem` da API (mostrada em toast pela tela).
   */
  async loginWithGoogle(idToken: string): Promise<LoginResult> {
    const body: GoogleLoginInfo = { idToken }

    return apiRequest<LoginResult>('POST', '/auth/google', { body, auth: false })
  }

  /**
   * `POST /auth/dev-login`, disponível só em Development/Homolog. O 404 típico do
   * ambiente de produção é traduzido para "Entrada de desenvolvimento indisponível".
   *
   * A primeira linha é uma **guarda de build** (FR-002, SC-008): o Vite substitui
   * `import.meta.env.VITE_DEV_LOGIN` por uma constante em tempo de compilação,
   * então na build de produção a comparação colapsa para `true` e o Rollup elimina
   * todo o corpo que envia `{ email, name }` a `/auth/dev-login`. Sem isso, a
   * rota e o DTO continuariam vivos no bundle principal — o `AuthContext` importa
   * este serviço sempre, e a flag de build só escondia o **formulário**
   * (`DevLoginForm`), não o chamador. `npm run verify:prod` é o que prova.
   */
  async loginDev(email: string, name: string): Promise<LoginResult> {
    if (import.meta.env.VITE_DEV_LOGIN !== 'true') {
      throw new ApiError(404, i18n.t('errors.devLoginUnavailable'))
    }

    const body: DevLoginInfo = { email, name }

    try {
      return await apiRequest<LoginResult>('POST', '/auth/dev-login', { body, auth: false })
    } catch (err) {
      if (err instanceof NotFoundError) {
        throw new ApiError(404, i18n.t('errors.devLoginUnavailable'))
      }

      throw err
    }
  }
}

export const authService = new AuthService()
export default AuthService
