using System.Text.RegularExpressions;

namespace Ambrosio.Domain.ValueObjects;

/// <summary>
/// CPF como value object imutável (research §5): normaliza a entrada (qualquer pontuação é
/// descartada), valida os dois dígitos verificadores por módulo 11 e rejeita sequências repetidas
/// como <c>111.111.111-11</c>, e expõe <see cref="Digits"/>, <see cref="Masked"/> e
/// <see cref="Formatted"/>.
/// <para>
/// Regra pura de domínio: sem dependências de infraestrutura, o que garante que o hash seja
/// sempre calculado sobre o documento canônico (FR-007, FR-010).
/// </para>
/// </summary>
public sealed class Cpf : IEquatable<Cpf>
{
    /// <summary>Quantidade de dígitos de um CPF válido.</summary>
    private const int DIGIT_COUNT = 11;

    /// <summary>Tudo que não é dígito (pontos, traços, barras e espaços) é descartado.</summary>
    private static readonly Regex NonDigits = new(@"\D", RegexOptions.Compiled);

    private Cpf(string digits)
    {
        Digits = digits;
    }

    /// <summary>Os 11 dígitos, sem pontuação — forma canônica usada no hash (research §4).</summary>
    public string Digits { get; }

    /// <summary>
    /// Máscara usada em listagens: os 3 primeiros e os 2 últimos dígitos somem, restando os
    /// dígitos 4 a 9 (<c>***.982.247-**</c>) — SC-005.
    /// </summary>
    public string Masked => $"***.{Digits[3..6]}.{Digits[6..9]}-**";

    /// <summary>Apresentação completa, usada apenas no detalhe do cliente (FR-009).</summary>
    public string Formatted => $"{Digits[..3]}.{Digits[3..6]}.{Digits[6..9]}-{Digits[9..]}";

    /// <summary>
    /// Tenta normalizar e validar. <paramref name="cpf"/> recebe o objeto apenas quando válido.
    /// </summary>
    public static bool TryParse(string? value, out Cpf? cpf)
    {
        cpf = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var digits = NonDigits.Replace(value, string.Empty);

        if (digits.Length != DIGIT_COUNT)
        {
            return false;
        }

        // "111.111.111-11" passa no cálculo dos verificadores, mas não é um documento emitido:
        // a spec (Edge Cases) manda recusar.
        if (digits.Distinct().Count() == 1)
        {
            return false;
        }

        if (!CheckDigitIsValid(digits, 9) || !CheckDigitIsValid(digits, 10))
        {
            return false;
        }

        cpf = new Cpf(digits);
        return true;
    }

    /// <summary>Atalho para o validator FluentValidation (<c>Must(Cpf.IsValid)</c>, FR-007).</summary>
    public static bool IsValid(string? value) => TryParse(value, out _);

    /// <summary>
    /// Valida o dígito verificador na posição <paramref name="index"/> (0-based): 9 → primeiro
    /// verificador (pesos 10..2 sobre os 9 primeiros), 10 → segundo (pesos 11..2 sobre os 10).
    /// </summary>
    private static bool CheckDigitIsValid(string digits, int index)
    {
        var sum = 0;
        var weight = index + 1;

        for (var i = 0; i < index; i++)
        {
            sum += (digits[i] - '0') * weight--;
        }

        var remainder = sum % 11;

        // Módulo 11: resto 0 ou 1 significa dígito zero.
        var expected = remainder < 2 ? 0 : 11 - remainder;

        return digits[index] - '0' == expected;
    }

    /// <inheritdoc />
    public bool Equals(Cpf? other) => other is not null && Digits == other.Digits;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Cpf);

    /// <inheritdoc />
    public override int GetHashCode() => Digits.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Formatted;

    public static bool operator ==(Cpf? left, Cpf? right) => Equals(left, right);

    public static bool operator !=(Cpf? left, Cpf? right) => !Equals(left, right);
}
