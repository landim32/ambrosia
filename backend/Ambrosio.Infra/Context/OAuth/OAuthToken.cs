using OpenIddict.EntityFrameworkCore.Models;

namespace Ambrosio.Infra.Context.OAuth;

/// <summary>
/// Código de autorização, access token e refresh token (tabela <c>oauth_tokens</c>) — feature 007,
/// data-model §1.3. O <c>type</c> distingue os três; o <c>status</c> faz o controle de uso único.
/// </summary>
/// <remarks>
/// Ciclo de vida que a feature depende (R14): <c>valid</c> → <c>redeemed</c> quando o código ou o
/// refresh é usado; reuso de um <c>redeemed</c> revoga a família inteira, o que é a materialização
/// da rotação de refresh token exigida pelo ChatGPT. Linhas vencidas saem pela limpeza periódica
/// (<c>OAuthPruneService</c>, T017), não por cascade — o Princípio §V proíbe cascade.
/// <para>
/// <c>reference_id</c> guarda o hash do valor, nunca o valor: o access token é opaco-no-banco e o
/// refresh token nunca é lido em claro de lá (a string real vai só ao cliente no response do token).
/// </para>
/// </remarks>
public class OAuthToken : OpenIddictEntityFrameworkCoreToken<long, OAuthApplication, OAuthAuthorization>
{
}
