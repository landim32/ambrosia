using System.Text.Json.Serialization;

namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Resultado de anexar foto ao álbum (feature 009, FR-006). É o DTO que distingue "criei agora" de
/// "essa foto já estava aqui", que é o aviso que a vendedora precisa ver no arraste (cenário US2-5).
/// </summary>
public class CustomerPhotoInsertResult
{
    [JsonPropertyName("photo")]
    public CustomerPhotoInfo Photo { get; set; } = new();

    /// <summary><c>false</c> quando a mesma imagem já estava no álbum desta cliente.</summary>
    [JsonPropertyName("created")]
    public bool Created { get; set; }

    /// <summary>
    /// A imagem já aparece no álbum de <b>outra</b> cliente da mesma vendedora (edge case "mesma foto
    /// em duas clientes"). Não impede a anexação: só informa, porque a linha é por cliente.
    /// </summary>
    [JsonPropertyName("alsoInOtherCustomers")]
    public bool AlsoInOtherCustomers { get; set; }
}
