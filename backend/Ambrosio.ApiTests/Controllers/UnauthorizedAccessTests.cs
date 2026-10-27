using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ambrosio.ApiTests.Fixtures;
using FluentAssertions;
using Flurl.Http;
using Microsoft.IdentityModel.Tokens;

namespace Ambrosio.ApiTests.Controllers;

/// <summary>
/// T089 [US4]: SC-004 — nenhuma rota protegida responde sem token válido. Varre as operações
/// autenticadas da feature (contrato openapi.yaml) em quatro condições: sem cabeçalho
/// <c>Authorization</c>, com um token ilegível, com um JWT bem formado assinado por OUTRA chave
/// (o pior caso: estrutura correta, origem falsa) e com a assinatura legítima adulterada.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class UnauthorizedAccessTests
{
    /// <summary>
    /// Segredo do atacante, diferente do segredo de desenvolvimento da API. Um token assinado aqui
    /// é perfeitamente legível (3 partes, claims corretos, issuer e audiência certos) — só falha na
    /// verificação da assinatura.
    /// </summary>
    private const string ATTACKER_SECRET = "attacker-secret-key-with-at-least-32-chars!!";

    private const string ISSUER = "ambrosio";
    private const string AUDIENCE = "ambrosio";

    /// <summary>Única rota protegida que exige <c>multipart/form-data</c> (gravação pelo servidor, feature 004).</summary>
    private const string MULTIPART_PATH = "/images";

    /// <summary>Operações protegidas: verbo + caminho.</summary>
    public static TheoryData<string, string> ProtectedOperations => new()
    {
        { "GET", "/users/me" },
        { "GET", "/customers" },
        { "POST", "/customers" },
        { "GET", "/customers/1" },
        { "PUT", "/customers/1" },
        { "GET", "/orders" },
        { "POST", "/orders" },
        { "GET", "/orders/1" },
        { "PUT", "/orders/1" },

        // Feature 004: as cinco rotas de imagem são autenticadas. Sem o token ninguém declara envio,
        // confirma, consulta, grava pelo servidor nem apaga — nem descobrindo que o id existe.
        { "POST", "/images/uploads" },
        { "POST", "/images/uploads/1/confirm" },
        { "GET", "/images/1" },
        { "POST", "/images" },
        { "DELETE", "/images/1" },

        // Feature 006 (T037/T093): as seis rotas de importação do +Vendas. Sem token ninguém inicia
        // uma coleta com credenciais de terceiros nem descobre que uma execução existe.
        { "GET", "/imports/mais-vendas/defaults" },
        { "POST", "/imports/mais-vendas/runs" },
        { "GET", "/imports/mais-vendas/runs/current" },
        { "GET", "/imports/mais-vendas/runs/1" },
        { "GET", "/imports/mais-vendas/runs" },
        { "POST", "/imports/mais-vendas/runs/1/cancel" }
    };

    private readonly ApiTestFixture _fixture;

    public UnauthorizedAccessTests(ApiTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(ProtectedOperations))]
    public async Task Operation_ShouldReturn401_WithoutAuthorizationHeader(string method, string path)
    {
        // Act: nenhum cabeçalho Authorization ([Authorize] está declarado na classe do controller).
        var status = await SendAsync(method, path, authorizationHeader: null);

        // Assert: 401 com corpo vazio, emitido pelo middleware JwtBearer (research §8).
        status.Should().Be(401, $"{method} {path} respondeu sem exigir token");
    }

    [Theory]
    [MemberData(nameof(ProtectedOperations))]
    public async Task Operation_ShouldReturn401_WithAGarbageToken(string method, string path)
    {
        // Act: texto que nem é JWT — o JwtBearer recusa na decodificação.
        var status = await SendAsync(method, path, "Bearer nao.e.um.jwt");

        // Assert
        status.Should().Be(401, $"{method} {path} aceitou um token ilegível");
    }

    [Theory]
    [MemberData(nameof(ProtectedOperations))]
    public async Task Operation_ShouldReturn401_WithATokenSignedByAnotherKey(string method, string path)
    {
        // Arrange: JWT válido em tudo menos na assinatura, gerado neste teste (T089).
        var forged = WriteToken(ATTACKER_SECRET, userId: 999);

        // Act
        var status = await SendAsync(method, path, $"Bearer {forged}");

        // Assert: se a API confiasse no formato ou validasse com a chave errada, isto daria 2xx/404.
        status.Should().Be(401, $"{method} {path} aceitou token forjado");
    }

    [Theory]
    [MemberData(nameof(ProtectedOperations))]
    public async Task Operation_ShouldReturn401_WithATamperedRealToken(string method, string path)
    {
        // Arrange: token legítimo do usuário A com um caractere da assinatura trocado.
        var tampered = Helpers.JwtHelpers.TamperSignature(_fixture.TokenA);

        // Act
        var status = await SendAsync(method, path, $"Bearer {tampered}");

        // Assert
        status.Should().Be(401, $"{method} {path} aceitou assinatura adulterada");
    }

    /// <summary>
    /// Um id de usuário real (o do A) também não engana a API: a identidade nunca sai do conteúdo
    /// autodeclarado do token sem a assinatura conferida — e o 401 não vaza o e-mail.
    /// </summary>
    [Fact]
    public async Task ForgedTokenWithAValidUserId_ShouldNotRevealAnything()
    {
        // Arrange
        var forged = WriteToken(ATTACKER_SECRET, _fixture.UserIdA);

        // Act
        var response = await _fixture
            .CreateRequestWithRawAuthorization("/users/me", $"Bearer {forged}")
            .AllowAnyHttpStatus()
            .GetAsync();

        // Assert
        response.StatusCode.Should().Be(401);

        var body = await response.GetStringAsync();
        body.Should().NotContain(_fixture.EmailA);
    }

    [Fact]
    public async Task ThePublicLoginRoutes_ShouldStayAnonymous()
    {
        // Contrato inverso: /auth/login segue acessível sem token — depois da 003 é o caminho
        // real de entrada (research §1). Se um filtro global quebrar isso, todos os testes HTTP
        // falham aqui primeiro.
        var email = $"anon+{Guid.NewGuid():N}@exemplo.com";

        await _fixture.RegisterAsync(email, "Anônimo", _fixture.TestPassword);

        var response = await _fixture.PostAsync("/auth/login", string.Empty,
            new Ambrosio.DTO.Auth.LoginInfo { Email = email, Password = _fixture.TestPassword });

        response.StatusCode.Should().Be(200);
    }

    /// <summary>
    /// Envia a operação com um cabeçalho Authorization arbitrário (ou nenhum) e devolve só o
    /// status: o corpo dos 401 é vazio por definição no JwtBearer.
    /// </summary>
    private async Task<int> SendAsync(string method, string path, string? authorizationHeader)
    {
        // Os builders do fixture montam a URL (caminho + query) sem escapar as barras.
        var request = authorizationHeader is null
            ? _fixture.CreateAnonymousRequest(path)
            : _fixture.CreateRequestWithRawAuthorization(path, authorizationHeader);

        var response = method switch
        {
            "GET" => await request.AllowAnyHttpStatus().GetAsync(),
            // POST /images só aceita multipart ([Consumes]): o ASP.NET Core aplica essa restrição na
            // seleção do endpoint, ANTES da autorização, e um corpo JSON morreria em 415 sem o token
            // ser conferido. O teste é de autenticação, então manda o formato que a rota aceita.
            "POST" when path == MULTIPART_PATH => await request.AllowAnyHttpStatus()
                .PostMultipartAsync(form => form.AddString("mimeType", "image/jpeg")),
            "POST" => await request.AllowAnyHttpStatus().PostJsonAsync(new { }),
            "PUT" => await request.AllowAnyHttpStatus().PutJsonAsync(new { }),
            "DELETE" => await request.AllowAnyHttpStatus().DeleteAsync(),
            _ => throw new InvalidOperationException($"Verbo não coberto pelo teste: {method}")
        };

        return response.StatusCode;
    }

    private static string WriteToken(string secret, long userId)
    {
        var now = DateTime.UtcNow;

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: ISSUER,
            audience: AUDIENCE,
            claims:
            [
                new Claim("sub", userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim("email", "invasor@exemplo.com"),
                new Claim("name", "Invasor")
            ],
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
