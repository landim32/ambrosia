using OpenIddict.EntityFrameworkCore.Models;

namespace Ambrosio.Infra.Context.OAuth;

/// <summary>
/// Aplicação/cliente do authorization server (tabela <c>oauth_applications</c>) — feature 007,
/// data-model §1.1. Uma linha por <c>client_id</c>: uma para o documento CIMD do ChatGPT e uma por
/// registro DCR.
/// </summary>
/// <remarks>
/// Herda do modelo genérico do OpenIddict com chave <see cref="long"/> (R15) — o padrão da biblioteca
/// é <c>string</c>, que o Princípio §V não aceita (PK <c>bigint identity</c>). Os campos de protocolo
/// (<c>ClientId</c>, <c>ClientType</c>, <c>RedirectUris</c>, <c>Permissions</c>, <c>JsonWebKeySet</c>,
/// <c>ConcurrencyToken</c>) vêm da base; aqui entram só as duas colunas que o OpenIddict não conhece
/// e que este feature precisa para auditoria e cache.
/// </remarks>
public class OAuthApplication
    : OpenIddictEntityFrameworkCoreApplication<long, OAuthAuthorization, OAuthToken>
{
    /// <summary>
    /// Origem do cadastro: 1 = CIMD (documento baixado da URL do <c>client_id</c>), 2 = DCR
    /// (<c>POST /auth/connect/register</c>). Distinguir os dois importa porque o cliente CIMD é
    /// recriado/atualizado quando o documento muda, e o de DCR não tem documento para reler.
    /// </summary>
    public int RegistrationSource { get; set; }

    /// <summary>
    /// Instante UTC da última leitura bem-sucedida do documento CIMD. Base da revalidação do cache:
    /// sem ele, toda requisição de autorização baixaria o documento de novo.
    /// </summary>
    public DateTime? MetadataFetchedAt { get; set; }

    /// <summary>
    /// Criação da linha, em UTC. O OpenIddict não mantém datas de aplicação, então quem grava é o
    /// caminho de registro (DCR ou CIMD) — dado de auditoria, não de protocolo.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Última alteração da linha, em UTC.</summary>
    public DateTime UpdatedAt { get; set; }
}
