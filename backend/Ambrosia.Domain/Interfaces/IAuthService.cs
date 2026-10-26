using Ambrosia.DTO.Auth;

namespace Ambrosia.Domain.Interfaces;

/// <summary>
/// Fluxos de login da feature (FR-001, FR-002). Ambos terminam com um JWT do Ambrosia e o
/// perfil do usuário criado ou reutilizado.
/// </summary>
public interface IAuthService
{
    /// <summary>Troca um ID token do Google por um JWT do Ambrosia.</summary>
    Task<LoginResult> LoginWithGoogleAsync(GoogleLoginInfo info);

    /// <summary>
    /// Emissão direta de token para desenvolvimento (research §2). O controller só expõe a rota
    /// em Development e Homolog.
    /// </summary>
    Task<LoginResult> DevLoginAsync(DevLoginInfo info);
}
