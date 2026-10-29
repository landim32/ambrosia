using Ambrosio.API.Extensions;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.CustomerPhotos;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosio.API.Controllers;

/// <summary>
/// Álbum de fotos da cliente (feature 009): grade por cursor, descrição, favorita, exclusão e foto de
/// perfil. O <c>userId</c> vem <b>somente</b> do claim <c>sub</c> e o <c>customerId</c> da path; nenhum
/// corpo transporta o dono, e recurso de outra vendedora responde 404 com a mesma mensagem de "não
/// existe" (FR-025, SC-006).
/// <para>
/// <b>Nenhuma rota aqui recebe arquivo.</b> Os bytes entram pela rota de imagem da feature 004
/// (<c>POST /images</c>) ou, na conversa, por <c>IImageService.StoreFromServerAsync</c> — e o servidor
/// jamais decodifica a foto (research R2/R7). Por isso não há <c>[RequestSizeLimit]</c>, multipart nem
/// motivo de recusa de conteúdo neste controller.
/// </para>
/// <para>
/// A rota avulsa de renovação de URL de uma foto também não existe: <c>GET /images/{imageId}</c> já
/// renova qualquer imagem (research R10), o que deixa a paridade com as ferramentas do MCP nominal.
/// </para>
/// </summary>
[Route("customers/{customerId:long}")]
[Authorize]
public class CustomerPhotosController : ApiControllerBase
{
    private readonly ICustomerPhotoService _photoService;

    public CustomerPhotosController(ICustomerPhotoService photoService)
    {
        _photoService = photoService;
    }

    /// <summary>
    /// Grade de fotos da cliente, da mais recente para a mais antiga, por cursor (FR-010, research R8).
    /// A <c>url</c> de cada item é a do arquivo <b>original</b> — não há miniatura gerada no servidor
    /// (research R3) — e é sempre assinada nesta resposta, nunca persistida.
    /// </summary>
    /// <param name="customerId">Identificador da cliente, da path.</param>
    /// <param name="query">Cursor, tamanho da página (1-60, padrão 24) e filtro de favoritas.</param>
    /// <response code="200">Página de fotos; <c>nextCursor</c> nulo no fim da lista.</response>
    /// <response code="400">Paginação inválida.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente inexistente ou de outra vendedora.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    [HttpGet("photos")]
    [ProducesResponseType(typeof(CustomerPhotoPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> List(long customerId, [FromQuery] CustomerPhotoListQuery query)
    {
        try
        {
            return Ok(await _photoService.ListAsync(User.GetUserId(), customerId, query));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (StorageUnavailableException ex)
        {
            return StorageUnavailableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Anexa ao álbum uma imagem que o usuário já gravou (FR-001, FR-006). Foto repetida na mesma
    /// cliente não cria segunda linha: vem a existente com <c>created = false</c>, e a tela avisa
    /// "essa foto já estava no álbum".
    /// </summary>
    /// <param name="customerId">Identificador da cliente, da path.</param>
    /// <param name="info">Imagem a anexar, descrição opcional e origem (só <c>web</c> ou <c>api</c>).</param>
    /// <response code="201">Foto nova no álbum.</response>
    /// <response code="200">A mesma imagem já estava no álbum desta cliente.</response>
    /// <response code="400">Formato inválido, ou imagem ainda pendente/recusada.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente ou imagem inexistente, ou de outra vendedora.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    [HttpPost("photos")]
    [ProducesResponseType(typeof(CustomerPhotoInsertResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CustomerPhotoInsertResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Attach(long customerId, [FromBody] CustomerPhotoInsertInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            var result = await _photoService.AttachAsync(User.GetUserId(), customerId, info);

            if (!result.Created)
            {
                return Ok(result);
            }

            Response.Headers.Location =
                $"/customers/{customerId}/photos/{result.Photo.PhotoId}";

            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (StorageUnavailableException ex)
        {
            return StorageUnavailableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Cria, troca ou apaga a descrição da foto (FR-007). Enviar <c>null</c> ou texto vazio apaga — por
    /// isso não existe valor "sem mudança".
    /// </summary>
    /// <param name="customerId">Identificador da cliente, da path.</param>
    /// <param name="photoId">Identificador da foto.</param>
    /// <param name="info">Descrição (até 500 caracteres) ou <c>null</c>.</param>
    /// <response code="200">Foto atualizada.</response>
    /// <response code="400">Descrição acima de 500 caracteres.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente ou foto inexistente, ou de outra vendedora.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    [HttpPut("photos/{photoId:long}/description")]
    [ProducesResponseType(typeof(CustomerPhotoInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateDescription(
        long customerId, long photoId, [FromBody] CustomerPhotoDescriptionInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            return Ok(await _photoService.UpdateDescriptionAsync(
                User.GetUserId(), customerId, photoId, info));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (StorageUnavailableException ex)
        {
            return StorageUnavailableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Marca a foto como favorita (FR-008). Idempotente: favoritar o que já é favorito não mexe em
    /// <c>updatedAt</c>, e a conversa pode repetir o pedido sem efeito colateral.
    /// </summary>
    /// <param name="customerId">Identificador da cliente, da path.</param>
    /// <param name="photoId">Identificador da foto.</param>
    /// <response code="200">Foto favorita.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente ou foto inexistente, ou de outra vendedora.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    [HttpPut("photos/{photoId:long}/favorite")]
    [ProducesResponseType(typeof(CustomerPhotoInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Favorite(long customerId, long photoId)
    {
        try
        {
            return Ok(await _photoService.SetFavoriteAsync(User.GetUserId(), customerId, photoId, true));
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (StorageUnavailableException ex)
        {
            return StorageUnavailableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Desmarca a favorita (FR-008). Dois verbes porque a paridade com as ferramentas do MCP é 1:1 com
    /// as operações REST (research R11).
    /// </summary>
    /// <param name="customerId">Identificador da cliente, da path.</param>
    /// <param name="photoId">Identificador da foto.</param>
    /// <response code="200">Foto sem coração.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente ou foto inexistente, ou de outra vendedora.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    [HttpDelete("photos/{photoId:long}/favorite")]
    [ProducesResponseType(typeof(CustomerPhotoInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Unfavorite(long customerId, long photoId)
    {
        try
        {
            return Ok(await _photoService.SetFavoriteAsync(User.GetUserId(), customerId, photoId, false));
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (StorageUnavailableException ex)
        {
            return StorageUnavailableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Remove a foto do álbum (FR-009). O arquivo sai do storage quando nenhuma outra foto, perfil ou
    /// item de pedido o usa; se a foto era a origem da foto de perfil, a cliente volta às iniciais
    /// (FR-015) — tudo sem cascata no banco (Princípio V).
    /// </summary>
    /// <param name="customerId">Identificador da cliente, da path.</param>
    /// <param name="photoId">Identificador da foto.</param>
    /// <response code="204">Foto removida.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente ou foto inexistente, ou de outra vendedora.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    [HttpDelete("photos/{photoId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Delete(long customerId, long photoId)
    {
        try
        {
            await _photoService.DeleteAsync(User.GetUserId(), customerId, photoId);

            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (StorageUnavailableException ex)
        {
            return StorageUnavailableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Define a foto de perfil da cliente (FR-011 a FR-014). <c>imageId</c> é o recorte quadrado de até
    /// 512 × 512 <b>gerado no navegador</b> e já gravado como imagem; sem ele, o círculo usa o arquivo
    /// da própria foto, que é o caso da conversa no ChatGPT (FR-013).
    /// </summary>
    /// <param name="customerId">Identificador da cliente, da path.</param>
    /// <param name="info">Foto de origem e recorte opcional.</param>
    /// <response code="200">Perfil definido, com as URLs do recorte e do original.</response>
    /// <response code="400">Formato inválido, ou imagem ainda pendente/recusada.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente, foto ou imagem inexistente, ou de outra vendedora.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    [HttpPut("profile-photo")]
    [ProducesResponseType(typeof(CustomerProfilePhotoInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SetProfilePhoto(long customerId, [FromBody] ProfilePhotoUpdateInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            return Ok(await _photoService.SetProfileAsync(User.GetUserId(), customerId, info));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (StorageUnavailableException ex)
        {
            return StorageUnavailableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Remove a foto de perfil sem excluir a foto do álbum (FR-015). A cliente volta a mostrar as
    /// iniciais até que outra foto seja escolhida.
    /// </summary>
    /// <param name="customerId">Identificador da cliente, da path.</param>
    /// <response code="204">Perfil removido.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente inexistente ou de outra vendedora.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    [HttpDelete("profile-photo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RemoveProfilePhoto(long customerId)
    {
        try
        {
            await _photoService.RemoveProfileAsync(User.GetUserId(), customerId);

            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (StorageUnavailableException ex)
        {
            return StorageUnavailableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
}
