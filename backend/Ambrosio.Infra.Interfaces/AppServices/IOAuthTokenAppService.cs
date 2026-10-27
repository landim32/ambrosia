using Ambrosio.DTO.Auth;

namespace Ambrosio.Infra.Interfaces.AppServices;

/// <summary>
/// Validação de ID token de um provedor OIDC externo (research §1). Implementada na Infra
/// (<c>GoogleOAuthTokenAppService</c>) para que o Domain não dependa da biblioteca do provedor.
/// </summary>
public interface IOAuthTokenAppService
{
    /// <summary>
    /// Valida assinatura, emissor, audiência e expiração do ID token.
    /// </summary>
    /// <returns>As claims do provedor, ou <c>null</c> quando o token é inválido/expirado.</returns>
    Task<OAuthUserInfo?> ValidateIdTokenAsync(string idToken);
}
