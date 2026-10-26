namespace Ambrosia.DTO.Enums;

/// <summary>
/// Provedor de autenticação que originou o usuário. Persistido como <c>integer</c>;
/// no JSON: <c>google</c> | <c>dev</c>.
/// </summary>
public enum AuthProvider
{
    /// <summary>Usuário criado a partir de um ID token do Google (OIDC).</summary>
    Google = 1,

    /// <summary>Usuário criado pelo <c>POST /auth/dev-login</c> (somente Development).</summary>
    Dev = 2
}
