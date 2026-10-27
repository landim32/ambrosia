using Ambrosio.DTO.Auth;
using Ambrosio.DTO.Settings;
using Ambrosio.Infra.Interfaces.AppServices;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace Ambrosio.Infra.AppServices;

/// <summary>
/// Validação do ID token do Google via OIDC (research §1): assinatura, emissor, audiência e
/// expiração são checadas pela biblioteca oficial do provedor, aqui na Infra — o Domain só
/// conhece <see cref="IOAuthTokenAppService"/>.
/// </summary>
public class GoogleOAuthTokenAppService : IOAuthTokenAppService
{
    private readonly AuthSettings _settings;

    public GoogleOAuthTokenAppService(IOptions<AuthSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <inheritdoc />
    public async Task<OAuthUserInfo?> ValidateIdTokenAsync(string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        // Sem client ID configurado não há audiência a validar: a rota existe, mas devolve o
        // mesmo erro de campo de um token inválido (evita uma exceção de configuração em 500).
        if (string.IsNullOrWhiteSpace(_settings.GoogleClientId))
        {
            return null;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new List<string> { _settings.GoogleClientId }
                });

            return new OAuthUserInfo
            {
                Email = payload.Email,
                EmailVerified = payload.EmailVerified,
                Name = payload.Name,
                Subject = payload.Subject
            };
        }
        catch (Exception ex) when (IsRejection(ex))
        {
            // Token malformado, assinado por outra chave, com audiência/emissor divergente ou
            // expirado: o serviço traduz em erro de validação no campo `idToken` (T041), nunca em
            // 401 — a credencial apresentada não é deste sistema (research §8).
            return null;
        }
    }

    /// <summary>
    /// A biblioteca sinaliza rejeição com <see cref="InvalidJwtException"/>; falhas de transporte
    /// ao buscar as chaves públicas do Google (sem rede) também são indisponibilidade do
    /// provedor, não do Ambrosio, e seguem o mesmo caminho.
    /// </summary>
    private static bool IsRejection(Exception ex) =>
        ex is InvalidJwtException
            or ArgumentException
            or HttpRequestException
            or TaskCanceledException
            or NotSupportedException
            or System.Text.Json.JsonException;
}
