using Ambrosio.Infra.Context.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer.Auth.Stores;

/// <summary>
/// Store de autorizações do OpenIddict com as operações em lote reescritas para o EF Core 9 (research
/// R19/R22), pelo mesmo motivo do <see cref="AmbrosioTokenStore"/>: o asset net8.0 da biblioteca chama
/// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> do EF Core 8, que não existem no EF Core 9.
/// </summary>
public sealed class AmbrosioAuthorizationStore(
    IMemoryCache cache,
    IOpenIddictEntityFrameworkCoreContext context,
    IOptionsMonitor<OpenIddictEntityFrameworkCoreOptions> options)
    : OpenIddictEntityFrameworkCoreAuthorizationStore<OAuthAuthorization, OAuthApplication, OAuthToken, long>(cache, context, options)
{
    /// <summary>
    /// Remove autorizações criadas antes do limite que não valem mais (revogadas ou ad hoc) e já não
    /// têm token — os tokens são limpos antes pelo store de tokens. Sem cascata no banco (§V), uma
    /// autorização com token ainda guardado fica para o próximo ciclo.
    /// </summary>
    public override async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var limit = threshold.UtcDateTime;
        var dbContext = await context.GetDbContextAsync(cancellationToken);

        return await dbContext.Set<OAuthAuthorization>()
            .Where(a => a.CreationDate < limit)
            .Where(a => a.Status != Statuses.Valid || a.Type == AuthorizationTypes.AdHoc)
            .Where(a => !a.Tokens.Any())
            .ExecuteDeleteAsync(cancellationToken);
    }

    public override async ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var key = ConvertIdentifierFromString(identifier);

        return await RevokeWhereAsync(a => a.Application!.Id == key, cancellationToken);
    }

    public override async ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        await RevokeWhereAsync(a => a.Subject == subject, cancellationToken);

    private async Task<long> RevokeWhereAsync(
        System.Linq.Expressions.Expression<Func<OAuthAuthorization, bool>> filter,
        CancellationToken cancellationToken)
    {
        var dbContext = await context.GetDbContextAsync(cancellationToken);

        return await dbContext.Set<OAuthAuthorization>()
            .Where(filter)
            .Where(a => a.Status != Statuses.Revoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Status, Statuses.Revoked), cancellationToken);
    }
}
