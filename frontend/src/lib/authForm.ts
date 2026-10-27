import { ApiError } from '@/Services/apiClient'

/** Erros de campo que a API sabe apontar (`ErrorResult.erros[].campo`). */
export interface ApiFieldErrors {
  name?: string
  email?: string
  password?: string
}

/** Erros de campo do formulário de cadastro (inclui a confirmação, só local). */
export interface AuthFieldErrors extends ApiFieldErrors {
  confirmPassword?: string
}

/** Valores brutos do formulário de entrada (nenhum corte feito antes da validação). */
export interface LoginFormValues {
  email: string
  password: string
}

/** Valores brutos do formulário de cadastro. */
export interface RegisterFormValues extends LoginFormValues {
  name: string
  confirmPassword: string
}

/** Limite de nome da FR-003 (mais estrito que a coluna `name varchar(200)`). */
const NAME_MAX_LENGTH = 120

/** Limite de e-mail da FR-003 (mais estrito que a coluna `email varchar(260)`). */
const EMAIL_MAX_LENGTH = 254

/** Faixa de senha do cadastro — a mesma do `RegisterInfoValidator` da API. */
const PASSWORD_MIN_LENGTH = 8
const PASSWORD_MAX_LENGTH = 128

/**
 * Formato básico de e-mail no cliente: algo antes do `@`, um rótulo e um domínio.
 * Não é a lista de TLDs — quem decide é o validator da API; aqui o objetivo é só
 * não enviar um óbvio inválido (data-model §8).
 */
const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/**
 * `state` levado por `RequireAuth` e por `AuthContext.logout` ao redirecionar.
 * Mora aqui (e não em `RequireAuth`) para que `/login` e `/cadastro` compartilhem
 * a leitura sem importar a guarda de rotas — e, com ela, os contextos de dados.
 */
interface ReturnToState {
  returnTo?: string
}

/**
 * Destino a restaurar depois do login: `location.state.returnTo`. Aceita apenas
 * string não vazia — qualquer outro valor (estado corrompido pelo histórico do
 * navegador, `null`, número) é ignorado e a tela usa o destino padrão.
 */
export const returnToOf = (state: unknown): string | undefined => {
  if (typeof state !== 'object' || state === null) return undefined

  const { returnTo } = state as ReturnToState

  return typeof returnTo === 'string' && returnTo !== '' ? returnTo : undefined
}

/**
 * Erros de campo devolvidos pela API (400 com `ErrorResult.erros[]`) traduzidos
 * para o campo do formulário. O nome chega em camelCase do
 * `ApiControllerBase.ToCamelCaseField`, mas é comparado sem diferenciar
 * maiúsculas por segurança. Um campo que o formulário não conhece (ex.: `body`)
 * é ignorado aqui — a `mensagem` geral dele é o que vai no `role="alert"`.
 */
export const fieldErrorsOf = (err: unknown): ApiFieldErrors => {
  if (!(err instanceof ApiError) || err.erros.length === 0) return {}

  const found: ApiFieldErrors = {}

  for (const fieldError of err.erros) {
    const field = fieldError.campo.toLowerCase()

    if (field === 'name' || field === 'email' || field === 'password') {
      found[field] = fieldError.mensagem
    }
  }

  return found
}

/** Validação do e-mail: obrigatório, formato básico, ≤ 254 (medidos com o corte). */
const emailError = (email: string): string | undefined => {
  const trimmed = email.trim()

  if (trimmed === '') return 'validation.emailRequired'
  if (!EMAIL_SHAPE.test(trimmed)) return 'validation.emailInvalid'
  if (trimmed.length > EMAIL_MAX_LENGTH) return 'validation.emailTooLong'

  return undefined
}

/**
 * Formulário de entrada (FR-002, data-model §8). O **comprimento da senha não é
 * validado**: qualquer senha errada cai no 401 genérico da API, e exigir 8..128
 * aqui recusaria localmente a senha de uma conta antiga — o erro tem de vir da
 * API, com a mensagem única, para não vazar informação sobre a conta.
 */
export const validateLoginForm = (values: LoginFormValues): ApiFieldErrors => {
  const errors: ApiFieldErrors = {}
  const email = emailError(values.email)

  if (email !== undefined) errors.email = email
  if (values.password === '') errors.password = 'validation.passwordRequired'

  return errors
}

/**
 * Formulário de cadastro (FR-003, data-model §8). Espelha o `RegisterInfoValidator`
 * para que nenhum envio inútil chegue à API. A senha é medida **sem cortar**:
 * espaços nas pontas fazem parte da senha (edge case da spec) e o mesmo valor
 * precisa valer no login.
 */
export const validateRegisterForm = (values: RegisterFormValues): AuthFieldErrors => {
  const errors: AuthFieldErrors = {}
  const name = values.name.trim()
  const email = emailError(values.email)

  if (name === '') errors.name = 'validation.nameRequired'
  else if (name.length > NAME_MAX_LENGTH) errors.name = 'validation.nameTooLong'

  if (email !== undefined) errors.email = email

  if (values.password.length < PASSWORD_MIN_LENGTH || values.password.length > PASSWORD_MAX_LENGTH) {
    errors.password = 'validation.passwordLength'
  } else if (values.confirmPassword !== values.password) {
    // Só confere quando a senha em si já é válida: duas senhas curtas e diferentes
    // devem apontar o comprimento, que é o problema real do campo.
    errors.confirmPassword = 'validation.passwordMismatch'
  }

  return errors
}
