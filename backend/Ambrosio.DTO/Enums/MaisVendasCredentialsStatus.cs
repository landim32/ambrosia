namespace Ambrosio.DTO.Enums;

/// <summary>
/// Situação das credenciais do +Vendas guardadas (feature 008, data-model §1). Persistido como
/// <c>integer</c> com <c>DEFAULT 0</c>; no JSON em camelCase (<c>none</c> | <c>valid</c> |
/// <c>needsUpdate</c>).
/// </summary>
public enum MaisVendasCredentialsStatus
{
    /// <summary>Nada cadastrado — a vendedora ainda não salvou login e senha (FR-004).</summary>
    None = 0,

    /// <summary>Cadastradas e aceitas na última execução; é o estado depois de salvar ou de um aceite.</summary>
    Valid = 1,

    /// <summary>
    /// O +Vendas recusou o login numa execução (FR-007). Não bloqueia o início: a senha pode voltar a
    /// ser aceita, e a tela e o plugin só orientam a vendedora a conferir a senha.
    /// </summary>
    NeedsUpdate = 2
}
