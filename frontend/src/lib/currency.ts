const currencyFormatter = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
})

/**
 * Moeda em reais (FR-019): `1234.56` → `R$ 1.234,56`; `-1234.56` → `-R$ 1.234,56`.
 * O separador depois do `R$` vem do `Intl` (NBSP/NNBSP conforme a ICU) — as telas
 * só exibem o texto; os testes normalizam para comparar.
 */
export const formatCurrency = (value: number): string =>
  currencyFormatter.format(Number.isFinite(value) ? value : 0)
