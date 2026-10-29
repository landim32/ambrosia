using System.Text.Json.Serialization;

namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Página da grade de fotos (feature 009, research R8). Paginação por <b>cursor</b>, não por página
/// numérica: fotos entram no topo enquanto a vendedora rola (arraste e conversa), e com offset cada
/// foto nova empurraria itens e duplicaria células na grade.
/// </summary>
public class CustomerPhotoPage
{
    /// <summary>Um a mais do que o pedido é lido para saber se há próxima página; este array é o corte.</summary>
    [JsonPropertyName("items")]
    public List<CustomerPhotoInfo> Items { get; set; } = [];

    /// <summary>Cursor da próxima página; <c>null</c> no fim da lista.</summary>
    [JsonPropertyName("nextCursor")]
    public long? NextCursor { get; set; }
}
