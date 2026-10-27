namespace Ambrosio.DTO.Enums;

/// <summary>
/// Provedor de autenticação que originou o usuário. Persistido como <c>integer</c>;
/// no JSON: <c>google</c> | <c>password</c>.
/// </summary>
public enum AuthProvider
{
    /// <summary>Usuário criado a partir de um ID token do Google (OIDC).</summary>
    Google = 1,

    /// <summary>Conta criada por <c>POST /auth/register</c> (e-mail e senha).</summary>
    Password = 2
}
