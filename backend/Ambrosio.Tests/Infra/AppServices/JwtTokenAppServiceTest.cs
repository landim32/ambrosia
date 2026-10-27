using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Ambrosio.DTO.Settings;
using Ambrosio.Infra.AppServices;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ambrosio.Tests.Infra.AppServices;

/// <summary>
/// T027 [US1]: emissão do JWT do Ambrosio (research §3) — claims, issuer/audience,
/// expiração e recusa quando assinado por outra chave.
/// </summary>
public class JwtTokenAppServiceTest
{
    private const string SECRET = "dev-secret-key-with-at-least-32-characters!!";
    private const string OTHER_SECRET = "another-secret-key-with-at-least-32-chars!!";

    private static JwtTokenAppService CreateSut(int expirationMinutes = 480)
    {
        var options = Options.Create(new AuthSettings
        {
            JwtSecret = SECRET,
            Issuer = "ambrosio",
            Audience = "ambrosio",
            ExpirationMinutes = expirationMinutes
        });

        return new JwtTokenAppService(options);
    }

    private static TokenValidationParameters ValidationParameters(string secret) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "ambrosio",
        ValidAudience = "ambrosio",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// Handler de teste espelhando o pipeline real da API (Program.cs, T025):
    /// <c>MapInboundClaims = false</c> mantém o claim <c>sub</c> sem renomeá-lo para
    /// <c>ClaimTypes.NameIdentifier</c>. Sem essa linha o handler padrão da biblioteca reescreve
    /// o nome do claim e a leitura de <c>sub</c> falharia por causa do teste, não da API.
    /// </summary>
    private static JwtSecurityTokenHandler CreateHandler() => new() { MapInboundClaims = false };

    [Fact]
    public void Generate_ShouldCarrySubEmailAndNameClaims()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var (token, _) = sut.Generate(userId: 42, email: "ana@exemplo.com", name: "Ana");

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("42", jwt.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal("ana@exemplo.com", jwt.Claims.First(c => c.Type == "email").Value);
        Assert.Equal("Ana", jwt.Claims.First(c => c.Type == "name").Value);
    }

    [Fact]
    public void Generate_ShouldUseConfiguredIssuerAndAudience()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var (token, _) = sut.Generate(1, "a@b.com", "A");

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("ambrosio", jwt.Issuer);
        Assert.Equal("ambrosio", jwt.Audiences.Single());
    }

    [Fact]
    public void Generate_ShouldExpireAtNowPlusExpirationMinutes()
    {
        // Arrange
        var sut = CreateSut(expirationMinutes: 480);

        // Act
        var (token, expiresAt) = sut.Generate(1, "a@b.com", "A");

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        // exp = agora + ExpirationMinutes; tolera a janela de execução do próprio teste.
        var delta = jwt.ValidTo - DateTime.UtcNow;
        Assert.InRange(delta.TotalMinutes, 477, 483);
        Assert.True(Math.Abs((expiresAt - DateTime.UtcNow).TotalMinutes - 480) < 3);
    }

    [Fact]
    public void Generate_ShouldReturnExpiresAtMatchingTheExpClaim()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var (token, expiresAt) = sut.Generate(7, "ana@exemplo.com", "Ana");

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        // exp tem granularidade de segundos: o valor retornado deve coincidir (até 2s).
        Assert.True(Math.Abs((expiresAt - jwt.ValidTo).TotalSeconds) <= 2,
            $"ExpiresAt {expiresAt:O} difere de exp {jwt.ValidTo:O}");
    }

    [Fact]
    public void Validate_ShouldSucceed_WithTheSameSigningKey()
    {
        // Arrange
        var sut = CreateSut();
        var (token, _) = sut.Generate(9, "ana@exemplo.com", "Ana");
        var handler = CreateHandler();

        // Act
        var principal = handler.ValidateToken(token, ValidationParameters(SECRET), out _);

        // Assert
        Assert.Equal("9", principal.FindFirst("sub")!.Value);
        Assert.True(principal.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Validate_ShouldFail_WithADifferentSigningKey()
    {
        // Arrange
        var sut = CreateSut();
        var (token, _) = sut.Generate(9, "ana@exemplo.com", "Ana");
        var handler = CreateHandler();

        // Act & Assert: SC-004 — token assinado por outra chave não autentica.
        // SecurityTokenValidationException cobre tanto assinatura inválida quanto chave
        // não encontrada, cujas exceções variam entre versões da biblioteca.
        Assert.ThrowsAny<SecurityTokenValidationException>(
            () => handler.ValidateToken(token, ValidationParameters(OTHER_SECRET), out _));
    }
}
