using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ambrosia.DTO.Imports.Collector;

namespace Ambrosia.Domain.Common;

/// <summary>
/// Impressão digital do conteúdo de um pedido vindo do +Vendas (feature 006, FR-022, research §6).
/// <para>
/// O hash é o que decide "inalterado" vs "atualizado" numa reimportação, então o que entra e a ordem
/// em que entra são contrato: JSON canônico com chaves em ordem fixa, decimais com 2 casas em
/// <see cref="CultureInfo.InvariantCulture"/>, datas <c>AAAA-MM-DD</c>, strings com <c>Trim()</c> e
/// itens ordenados por <c>lineNo</c>. SHA-256 em hex minúsculo (cabe em <c>varchar(64)</c>).
/// </para>
/// <para>
/// <b>Não</b> entram <c>captured_at</c>, <c>image_id</c>, id de cliente nem qualquer identificador
/// interno: copiar a foto para o storage não é mudança de conteúdo da origem, e a URL de origem continua
/// no item (FR-040).
/// </para>
/// </summary>
public static class SourceHashCalculator
{
    /// <summary>Ordem fixa das chaves do pedido (research §6).</summary>
    private static readonly string[] OrderFields =
    [
        "orderNumber",
        "orderDate",
        "kind",
        "channel",
        "origin",
        "status",
        "total",
        "originalSaleOn",
        "cancelledOn",
        "externalId"
    ];

    /// <summary>Ordem fixa das chaves de cada linha do pedido.</summary>
    private static readonly string[] ItemFields =
    [
        "lineNo",
        "productName",
        "skuRef",
        "size",
        "price",
        "imageUrl"
    ];

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Hash do pedido + hash do CPF do comprador. O documento entra pelo hash já protegido, nunca em
    /// claro — o conteúdo de um pedido inclui <i>quem</i> comprou, e trocar o CPF do cliente de um
    /// pedido é mudança de conteúdo.
    /// </summary>
    /// <param name="order">Pedido normalizado vindo do coletor.</param>
    /// <param name="cpfHash">HMAC do CPF (research §4); <c>null</c> quando a origem não trouxe documento.</param>
    /// <returns>SHA-256 em hex minúsculo, 64 caracteres.</returns>
    public static string Compute(CollectedOrder order, string? cpfHash)
    {
        ArgumentNullException.ThrowIfNull(order);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BuildCanonicalJson(order, cpfHash))))
            .ToLowerInvariant();
    }

    /// <summary>
    /// O JSON canônico em si (a entrada do <see cref="Compute"/>), exposto para o rastro de depuração e
    /// para os testes poderem conferir a ordem das chaves sem reimplementar a serialização.
    /// </summary>
    public static string BuildCanonicalJson(CollectedOrder order, string? cpfHash)
    {
        ArgumentNullException.ThrowIfNull(order);

        var buffer = new StringBuilder(512);

        buffer.Append('{');

        // Campos escalares do pedido, na ordem fixa.
        AppendKeyValue(buffer, "orderNumber", Normalize(order.OrderNumber));
        buffer.Append(',');
        AppendKeyValue(buffer, "orderDate", order.OrderDate.ToString("yyyy-MM-dd"));
        buffer.Append(',');
        AppendKeyValue(buffer, "kind", order.Kind.ToString().ToLowerInvariant());
        buffer.Append(',');
        AppendKeyValue(buffer, "channel", Normalize(order.Channel));
        buffer.Append(',');
        AppendKeyValue(buffer, "origin", order.Origin?.ToString().ToLowerInvariant());
        buffer.Append(',');
        AppendKeyValue(buffer, "status", Normalize(order.Status));
        buffer.Append(',');
        AppendKeyValue(buffer, "total", Money(order.Total));
        buffer.Append(',');
        AppendKeyValue(buffer, "originalSaleOn", order.OriginalSaleOn?.ToString("yyyy-MM-dd"));
        buffer.Append(',');
        AppendKeyValue(buffer, "cancelledOn", order.CancelledOn?.ToString("yyyy-MM-dd"));
        buffer.Append(',');
        AppendKeyValue(buffer, "externalId", Normalize(order.ExternalId));

        // Cliente: nome + hash do CPF. O nome é dado de origem e faz parte do conteúdo (mudança de
        // nome na origem é "atualizado" — US5). O CPF em claro nunca entra (FR-033).
        buffer.Append(",\"customer\":{");
        AppendKeyValue(buffer, "cpfHash", cpfHash);
        buffer.Append(',');
        AppendKeyValue(buffer, "name", Normalize(order.CustomerName));
        buffer.Append('}');

        // Itens ordenados por lineNo, com as chaves na ordem fixa.
        buffer.Append(",\"items\":[");

        var first = true;

        foreach (var item in order.Items.OrderBy(i => i.LineNo))
        {
            if (!first)
            {
                buffer.Append(',');
            }

            first = false;

            buffer.Append('{');
            AppendKeyValue(buffer, ItemFields[0], item.LineNo.ToString(System.Globalization.CultureInfo.InvariantCulture));
            buffer.Append(',');
            AppendKeyValue(buffer, ItemFields[1], Normalize(item.ProductName));
            buffer.Append(',');
            AppendKeyValue(buffer, ItemFields[2], Normalize(item.SkuRef));
            buffer.Append(',');
            AppendKeyValue(buffer, ItemFields[3], Normalize(item.Size));
            buffer.Append(',');
            AppendKeyValue(buffer, ItemFields[4], Money(item.Price));
            buffer.Append(',');
            AppendKeyValue(buffer, ItemFields[5], Normalize(item.ImageUrl));
            buffer.Append('}');
        }

        buffer.Append("]}");

        return buffer.ToString();
    }

    /// <summary>
    /// Comparação campo a campo de um pedido já gravado com o snapshot da origem, para o relatório
    /// listar <c>changedFields</c> (FR-023). Devolve os nomes das chaves canônicas que mudaram, e
    /// <c>items</c> quando o conteúdo das linhas difere — sem listar linha por linha.
    /// </summary>
    /// <param name="current">Hash gravado no pedido.</param>
    /// <param name="incoming">Hash do snapshot que está chegando.</param>
    /// <returns>Lista de campos alterados (vazia quando os dois hashes são iguais).</returns>
    public static List<string> DiffFields(CollectedOrder current, CollectedOrder incoming, string? currentCpfHash, string? incomingCpfHash)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);

        var changed = new List<string>();

        AddIfDifferent(changed, "orderNumber", current.OrderNumber, incoming.OrderNumber);
        AddIfDifferent(changed, "orderDate", current.OrderDate, incoming.OrderDate);
        AddIfDifferent(changed, "kind", current.Kind, incoming.Kind);
        AddIfDifferent(changed, "channel", Normalize(current.Channel), Normalize(incoming.Channel));
        AddIfDifferent(changed, "origin", current.Origin, incoming.Origin);
        AddIfDifferent(changed, "status", Normalize(current.Status), Normalize(incoming.Status));
        AddIfDifferent(changed, "total", current.Total, incoming.Total);
        AddIfDifferent(changed, "originalSaleOn", current.OriginalSaleOn, incoming.OriginalSaleOn);
        AddIfDifferent(changed, "cancelledOn", current.CancelledOn, incoming.CancelledOn);
        AddIfDifferent(changed, "externalId", Normalize(current.ExternalId), Normalize(incoming.ExternalId));
        AddIfDifferent(changed, "customer", (currentCpfHash, Normalize(current.CustomerName)), (incomingCpfHash, Normalize(incoming.CustomerName)));

        if (!ItemsEqual(current, incoming))
        {
            changed.Add("items");
        }

        return changed;
    }

    private static bool ItemsEqual(CollectedOrder current, CollectedOrder incoming)
    {
        var left = current.Items.OrderBy(i => i.LineNo).ToList();
        var right = incoming.Items.OrderBy(i => i.LineNo).ToList();

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];

            if (a.LineNo != b.LineNo
                || !string.Equals(Normalize(a.ProductName), Normalize(b.ProductName), StringComparison.Ordinal)
                || !string.Equals(Normalize(a.SkuRef), Normalize(b.SkuRef), StringComparison.Ordinal)
                || !string.Equals(Normalize(a.Size), Normalize(b.Size), StringComparison.Ordinal)
                || Money(a.Price) != Money(b.Price)
                || !string.Equals(Normalize(a.ImageUrl), Normalize(b.ImageUrl), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddIfDifferent<T>(List<string> changed, string field, T left, T right)
    {
        if (!EqualityComparer<T>.Default.Equals(left, right))
        {
            changed.Add(field);
        }
    }

    /// <summary>Decimal de 2 casas, invariante, sem separador de milhar (contrato canônico).</summary>
    private static string Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary><c>Trim()</c> e nulo quando sobra vazio — "<c> </c>" e <c>null</c> são o mesmo conteúdo.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AppendKeyValue(StringBuilder buffer, string key, string? value)
    {
        buffer.Append(JsonSerializer.Serialize(key, CanonicalJsonOptions)).Append(':');

        if (value is null)
        {
            buffer.Append("null");
        }
        else
        {
            buffer.Append(JsonSerializer.Serialize(value, CanonicalJsonOptions));
        }
    }
}
