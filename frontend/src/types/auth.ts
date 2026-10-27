/** Auth Types — sessão e usuária vendedora (data-model §1) */

/** Usuária vendedora devolvida pela API em `UserInfo`. */
export interface UserInfo {
  id: number
  email: string
  name: string
  provider: 'google' | 'password'
  /** Instante ISO date-time de criação. */
  createdAt: string
}

/** Resposta de `POST /auth/google`, `POST /auth/login` e `POST /auth/register`. */
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

/** Corpo de `POST /auth/register` (data-model §3): a senha vai sem `trim()`. */
export interface RegisterInfo {
  name: string
  email: string
  password: string
}

/** Corpo de `POST /auth/login` (data-model §3). */
export interface LoginInfo {
  email: string
  password: string
}

/** O que vai ao `localStorage` na chave `login-with-metamask:auth`. */
export interface AuthSession {
  token: string
  expiresAt: string
  user: UserInfo
}
