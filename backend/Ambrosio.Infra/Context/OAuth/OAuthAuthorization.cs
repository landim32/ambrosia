using OpenIddict.EntityFrameworkCore.Models;

namespace Ambrosio.Infra.Context.OAuth;

/// <summary>
/// Consentimento da vendedora (tabela <c>oauth_authorizations</c>) — feature 007, data-model §1.2.
/// Guarda quem autorizou quais escopos para qual cliente, o que permite reenovar o acesso sem
/// pedir consentimento a cada religação (FR-006, R14).
/// </summary>
/// <remarks>
/// Sem colunas próprias além das do OpenIddict: <c>subject</c> (o <c>user_id</c> em texto),
/// <c>scopes</c>, <c>status</c> (<c>valid</c>/<c>revoked</c>), <c>type</c> (<c>permanent</c>/<c>ad-hoc</c>)
/// e <c>creation_date</c> já vêm da classe base. Revogar esta linha invalida os tokens ligados a ela.
/// </remarks>
public class OAuthAuthorization : OpenIddictEntityFrameworkCoreAuthorization<long, OAuthApplication, OAuthToken>
{
}
