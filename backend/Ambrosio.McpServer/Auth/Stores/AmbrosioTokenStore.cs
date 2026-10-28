using Ambrosio.Infra.Context.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer.Auth.Stores;

/// <summary>
/// Store de tokens do OpenIddict com as revogações em lote reescritas para o EF Core 9 (research R19).
/// </summary>
/// <remarks>
/// O asset net8.0 do OpenIddict 7.7.2 chama <c>RelationalQueryableExtensions.ExecuteUpdateAsync</c> do
/// EF Core 8, que não existe no assembly Relational do EF Core 9 do projeto: a revogação da família de
/// um refresh reutilizado e a revogação de uma conexão caíam com <c>MissingMethodException</c>, e o
/// <c>DisableBulkOperations()</c> não cobre estes métodos. Compilado aqui, o mesmo
/// <c>ExecuteUpdateAsync</c> liga no EF Core 9 e a semântica é a da biblioteca: marca como revogados
/// os tokens ainda não revogados.
/// </remarks>
public sealed class AmbrosioTokenStore(
    IMemoryCache cache,
    IOpenIddictEntityFrameworkCoreContext context,
    IOptionsMonitor<OpenIddictEntityFrameworkCoreOptions> options)
    : OpenIddictEntityFrameworkCoreTokenStore<OAuthToken, OAuthApplication, OAuthAuthorization, long>(cache, context, options)
{
    public override async ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var key = ConvertIdentifierFromString(identifier);

        return await RevokeWhereAsync(t => t.Authorization!.Id == key, cancellationToken);
    }

    public override async ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var key = ConvertIdentifierFromString(identifier);

        return await RevokeWhereAsync(t => t.Application!.Id == key, cancellationToken);
    }

    public override async ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        await RevokeWhereAsync(t => t.Subject == subject, cancellationToken);

    /// <summary>
    /// Remove tokens criados antes do limite que não servem mais: vencidos, revogados ou resgatados,
    /// ou ligados a uma autorização que deixou de valer (T017, mesma regra do OpenIddict).
    /// </summary>
    public override async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var limit = threshold.UtcDateTime;
        var now = DateTime.UtcNow;
        var dbContext = await context.GetDbContextAsync(cancellationToken);

        return await dbContext.Set<OAuthToken>()
            .Where(t => t.CreationDate < limit)
            .Where(t => (t.Status != Statuses.Inactive && t.Status != Statuses.Valid)
                        || (t.Authorization != null && t.Authorization.Status != Statuses.Valid)
                        || t.ExpirationDate < now)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<long> RevokeWhereAsync(
        System.Linq.Expressions.Expression<Func<OAuthToken, bool>> filter,
        CancellationToken cancellationToken)
    {
        var dbContext = await context.GetDbContextAsync(cancellationToken);

        return await dbContext.Set<OAuthToken>()
            .Where(filter)
            .Where(t => t.Status != Statuses.Revoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.Status, Statuses.Revoked), cancellationToken);
    }
}
