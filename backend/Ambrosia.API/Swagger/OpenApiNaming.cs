using System.Text;

namespace Ambrosia.API.Swagger;

/// <summary>
/// Convenções de nome que alinham o documento gerado ao contrato em
/// <c>specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml</c> (T094).
/// </summary>
internal static class OpenApiNaming
{
    /// <summary>
    /// camelCase de um identificador simples: <c>PageSize</c> → <c>pageSize</c>,
    /// <c>IdToken</c> → <c>idToken</c>, <c>URLValue</c> → <c>urlValue</c>.
    /// <para>
    /// Regra: um run inicial de maiúsculas desce todo; com um único maiúsculo ele desce sozinho
    /// (<c>Search</c> → <c>search</c>); com run maior, o último maiúsculo pertence à palavra
    /// seguinte e permanece (<c>URLValue</c> → <c>urlValue</c>). É a mesma regra usada por
    /// <see cref="Controllers.ApiControllerBase"/> nos nomes de campo do <c>ErrorResult</c> — a
    /// implementação única evita que query string, erros de validação e documentação divirjam.
    /// </para>
    /// </summary>
    public static string ToCamelCase(string? value)
    {
        if (string.IsNullOrEmpty(value) || !char.IsUpper(value[0]))
        {
            return value ?? string.Empty;
        }

        var end = 0;
        while (end < value.Length && char.IsUpper(value[end]))
        {
            end++;
        }

        var lowerCount = end switch
        {
            1 => 1,
            var length when length == value.Length => length,
            var length => length - 1
        };

        return new StringBuilder()
            .Append(value[..lowerCount].ToLowerInvariant())
            .Append(value[lowerCount..])
            .ToString();
    }

    /// <summary>
    /// Nome do schema OpenAPI. O padrão do Swashbuckle compõe o wrapper paginado como
    /// <c>CustomerInfoPagedResult</c>; o contrato define <c>PagedCustomerInfo</c>/<c>PagedOrderInfo</c>
    /// (T094). Os demais tipos mantêm o nome da classe, exatamente como hoje.
    /// </summary>
    public static string SchemaId(Type type)
    {
        if (type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(DTO.Common.PagedResult<>))
        {
            return $"Paged{type.GetGenericArguments()[0].Name}";
        }

        return type.Name;
    }
}
