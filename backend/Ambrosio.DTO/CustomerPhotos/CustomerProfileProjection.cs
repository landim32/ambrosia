namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Linha de perfil lida de <c>customers</c> junto com a imagem da foto de origem
/// (feature 009, T042). Serve uma única coisa: preencher <c>CustomerInfo.profilePhoto</c> para
/// <b>várias</b> clientes de uma vez, com uma consulta só — fazer isso cliente a cliente na
/// listagem seria um N+1 em toda tela de clientes.
/// </summary>
/// <param name="CustomerId">Cliente da linha.</param>
/// <param name="PhotoId">Foto do álbum que originou o perfil.</param>
/// <param name="CropImageId">Recorte feito no navegador; <c>null</c> quando o perfil usa a própria foto.</param>
/// <param name="AlbumImageId">Imagem da foto do álbum (o "original" do FR-014).</param>
public record CustomerProfileProjection(
    long CustomerId,
    long PhotoId,
    long? CropImageId,
    long AlbumImageId);
