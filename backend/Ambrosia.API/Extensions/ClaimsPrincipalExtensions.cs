using System.Security.Claims;

namespace Ambrosia.API.Extensions;

/// <summary>
/// Leitura do identificador do usuário autenticado. O <c>userId</c> vem <b>somente</b> do claim
/// <c>sub</c> do JWT (FR-021): nenhum DTO de entrada transporta <c>userId</c>, o que torna o
/// isolamento por usuário estrutural (research §13).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Nome do claim que carrega o <c>user_id</c> (mantido sem renomear por
    /// <c>MapInboundClaims = false</c> no JwtBearer — research §3).</summary>
    public const string UserIdClaimType = "sub";

    /// <summary>
    /// Extrai o <c>user_id</c> do token.
    /// </summary>
    /// <param name="user">Principal da requisição.</param>
    /// <returns>O identificador do usuário autenticado.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// Se o claim <c>sub</c> estiver ausente ou não for numérico (token válido emitido por outra
    /// versão da API, por exemplo).
    /// </exception>
    public static long GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst(UserIdClaimType)?.Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UnauthorizedAccessException("Token sem o claim de identificação do usuário.");
        }

        if (!long.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException("Identificação de usuário inválida no token.");
        }

        return userId;
    }
}
