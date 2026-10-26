using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.Interfaces.AppServices;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ambrosia.Infra.AppServices;

/// <summary>
/// Emissão do JWT do Ambrosia (research §3): HS256 simétrico, claims <c>sub</c>/<c>email</c>/
/// <c>name</c>, <c>iss</c>/<c>aud</c> vindos de <see cref="AuthSettings"/> e expiração de
/// <c>ExpirationMinutes</c>. O mesmo segredo é usado pelo middleware <c>AddJwtBearer</c> de
/// <c>Program.cs</c>, que faz a validação.
/// </summary>
public class JwtTokenAppService : IJwtTokenAppService
{
    private const string SUB_CLAIM = "sub";
    private const string EMAIL_CLAIM = "email";
    private const string NAME_CLAIM = "name";

    /// <summary>Ticks de um segundo — usado para truncar a expiração (o claim <c>exp</c> tem
    /// granularidade de segundos).</summary>
    private const long TICKS_PER_SECOND = TimeSpan.TicksPerSecond;

    private readonly AuthSettings _settings;

    public JwtTokenAppService(IOptions<AuthSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <inheritdoc />
    public (string Token, DateTime ExpiresAt) Generate(long userId, string email, string name)
    {
        // O claim `sub` é a única origem do userId nas chamadas seguintes (FR-021): numérico em
        // texto, lido por ClaimsPrincipalExtensions.GetUserId com MapInboundClaims = false.
        var claims = new List<Claim>
        {
            new(SUB_CLAIM, userId.ToString(CultureInfo.InvariantCulture)),
            new(EMAIL_CLAIM, email),
            new(NAME_CLAIM, name)
        };

        var now = DateTime.UtcNow;

        // `exp` é gravado em segundos desde a epoch: truncar o instante garante que o valor
        // devolvido no LoginResult bate exatamente com o claim do token (T027).
        var expiresAt = TruncateToSeconds(now.AddMinutes(_settings.ExpirationMinutes));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.JwtSecret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % TICKS_PER_SECOND), value.Kind);
}
