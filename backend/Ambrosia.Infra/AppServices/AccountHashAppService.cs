using System.Security.Cryptography;
using System.Text;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.Interfaces.AppServices;
using Microsoft.Extensions.Options;

namespace Ambrosia.Infra.AppServices;

/// <summary>
/// Hash do identificador da conta do +Vendas (feature 006, research §8): HMAC-SHA256 em hex minúsculo
/// sobre <c>"mais-vendas-account:" + identificador</c>, com o pepper já existente
/// (<c>CpfProtection:Pepper</c>).
/// <para>
/// O prefixo de domínio existe para o mesmo segredo nunca produzir o mesmo digest para dois usos
/// diferentes: um <c>cpf_hash</c> e um <c>account_hash</c> não podem colidir por construção, mesmo com
/// pepper compartilhado.
/// </para>
/// <para>
/// Separação deliberada de <see cref="ICpfProtectionAppService"/>: ali o valor precisa ser
/// recuperado (cifra autenticada) e aqui não — o vínculo é só comparação, e guardar o identificador em
/// claro seria uma credencial de terceiros em banco (FR-006).
/// </para>
/// </summary>
public class AccountHashAppService : IAccountHashAppService
{
    /// <summary>Prefixo de domínio que separa este uso de qualquer outro HMAC do mesmo pepper.</summary>
    private const string DOMAIN_PREFIX = "mais-vendas-account:";

    private readonly CpfProtectionSettings _settings;

    public AccountHashAppService(IOptions<CpfProtectionSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <inheritdoc />
    public string Hash(string accountIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountIdentifier);

        // Trim + minúsculas: o mesmo login digitado com caixa diferente tem de achar o mesmo vínculo
        // (research §8), senão a vendedora receberia "conta diferente da vinculada" por capricho do teclado.
        var normalized = DOMAIN_PREFIX + accountIdentifier.Trim().ToLowerInvariant();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.Pepper));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));

        // Convert.ToHexString devolve maiúsculas; a coluna é varchar(64) de hex minúsculo (data-model §2).
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
