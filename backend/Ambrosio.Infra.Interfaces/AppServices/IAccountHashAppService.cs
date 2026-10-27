namespace Ambrosio.Infra.Interfaces.AppServices;

/// <summary>
/// Hash do identificador da conta do +Vendas (feature 006, research §8): HMAC-SHA256 com o pepper do
/// ambiente e prefixo de domínio, em hex minúsculo.
/// <para>
/// Fica separado do <see cref="ICpfProtectionAppService"/> de propósito: aquele é exclusivo do CPF e
/// sua semântica (unicidade + recuperação) não deve ser confundida com a de um vínculo de conta de
/// terceiros. Aqui não há cifra — o identificador não precisa ser recuperado, só comparado.
/// </para>
/// </summary>
public interface IAccountHashAppService
{
    /// <summary>
    /// Hash do identificador da conta (o código da vendedora exibido pelo +Vendas, ex.: <c>S482</c>).
    /// Normalizado com <c>Trim().ToLowerInvariant()</c> antes, para o mesmo login digitado com caixa
    /// diferente produzir o mesmo vínculo.
    /// </summary>
    string Hash(string accountIdentifier);
}
