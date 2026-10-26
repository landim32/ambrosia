/** Auth Types — sessão e usuária vendedora (data-model §1) */

/** Usuária vendedora devolvida pela API em `UserInfo`. */
export interface UserInfo {
  id: number
  email: string
  name: string
  provider: 'google' | 'dev'
  /** Instante ISO date-time de criação. */
  createdAt: string
}

/** Resposta de `POST /auth/google` e `POST /auth/dev-login`. */
export interface LoginResult {
  token: string
  /** Instante ISO date-time de expiração do JWT. */
  expiresAt: string
  user: UserInfo
}

/** Corpo de `POST /auth/google`. */
export interface GoogleLoginInfo {
  idToken: string
}

/** Corpo de `POST /auth/dev-login` (Development/Homolog apenas). */
export interface DevLoginInfo {
  email: string
  name: string
}

/** O que vai ao `localStorage` na chave `login-with-metamask:auth`. */
export interface AuthSession {
  token: string
  expiresAt: string
  user: UserInfo
}
