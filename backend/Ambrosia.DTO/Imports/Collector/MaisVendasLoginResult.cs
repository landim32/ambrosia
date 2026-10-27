namespace Ambrosia.DTO.Imports.Collector;

/// <summary>Resultado de uma tentativa de login na origem (feature 006).</summary>
public enum MaisVendasLoginOutcome
{
    /// <summary>Credenciais aceitas; a sessão está autenticada e o identificador da conta é confiável.</summary>
    Accepted,

    /// <summary>
    /// Credenciais recusadas pela origem. Alimenta a contagem de recusas seguidas e o bloqueio
    /// (FR-008a) — nunca o motivo detalhado, nem o login.
    /// </summary>
    Refused,

    /// <summary>
    /// A origem pediu uma etapa que o coletor não executa de propósito (código 2FA, captcha): a
    /// execução falha com mensagem clara, sem tentar adivinhar (FR-046).
    /// </summary>
    UnsupportedStep
}

/// <summary>
/// Resposta do login na origem (feature 006, research §8).
/// </summary>
public sealed class MaisVendasLoginResult
{
    private MaisVendasLoginResult(MaisVendasLoginOutcome outcome, string? accountIdentifier)
    {
        Outcome = outcome;
        AccountIdentifier = accountIdentifier;
    }

    public MaisVendasLoginOutcome Outcome { get; }

    /// <summary>
    /// Identificador da conta na origem (o código da vendedora, ex.: <c>S482</c>), usado só para
    /// derivar o <c>account_hash</c> com pepper. <c>null</c> quando o login não foi aceito.
    /// </summary>
    public string? AccountIdentifier { get; }

    public static MaisVendasLoginResult Accepted(string? accountIdentifier) =>
        new(MaisVendasLoginOutcome.Accepted, accountIdentifier);

    public static MaisVendasLoginResult Refused() => new(MaisVendasLoginOutcome.Refused, null);

    public static MaisVendasLoginResult Unsupported() => new(MaisVendasLoginOutcome.UnsupportedStep, null);
}
