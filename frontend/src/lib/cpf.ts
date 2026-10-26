/**
 * CPF no frontend (FR-012/FR-022, T050).
 *
 * Nao existe funcao de "desmascarar": quem revela o CPF e a API, no campo `cpf`
 * do `CustomerDetailInfo` (perfil). Aqui so se reconhece/normaliza o mascarado
 * que a listagem exibe. O objetivo da ausente funcao inversa e estrutural — sem
 * ela, nenhum codigo consegue fabricar um CPF completo a partir do que a tela ja
 * tem (SC-003).
 */

/** Formato devolvido pela API em `cpfMasked` (Cpf.Masked, HU 001). */
const MASKED_PATTERN = /^\*\*\*\.\d{3}\.\d{3}-\*\*$/

/** Placeholder neutro para valor inesperado: nunca um CPF completo. */
const NEUTRAL_MASK = '***.***.***-**'

/** O texto ja esta no formato mascarado da API? */
export const isMaskedCpf = (value: string): boolean => MASKED_PATTERN.test(value)

/**
 * Garante que a LISTAGEM so exiba o mascarado. Se o valor recebido nao tem o
 * formato esperado (dado corrompido, alteracao de contrato), devolve o
 * placeholder em vez de ecoar o texto — assim um CPF completo nunca aparece em
 * lista nem no detalhe do pedido (FR-022).
 */
export const maskedCpfShape = (value: string): string => (isMaskedCpf(value) ? value : NEUTRAL_MASK)
