using OpenIddict.EntityFrameworkCore.Models;

namespace Ambrosio.Infra.Context.OAuth;

/// <summary>
/// Escopo anunciado pelo authorization server (tabela <c>oauth_scopes</c>) — feature 007, data-model §1.4.
/// São as permissões que a tela de consentimento lista em português (FR-003/FR-004) e o valor de
/// <c>scopes_supported</c> nos documentos de descoberta.
/// </summary>
/// <remarks>
/// A <c>resources</c> da classe base (array JSON) é o que amarra o escopo ao recurso protegido
/// (<c>Mcp:ResourceUrl</c>), fazendo o <c>aud</c> do access token sair certo sem handler extra.
/// <para>
/// Aqui a aridade genérica é só <c>&lt;long&gt;</c>: ao contrário de application/authorization/token,
/// <see cref="OpenIddictEntityFrameworkCoreScope{TKey}"/> não tem parâmetros para entidades
/// relacionadas — escopo não navega para ninguém.
/// </para>
/// </remarks>
public class OAuthScope : OpenIddictEntityFrameworkCoreScope<long>
{
}
