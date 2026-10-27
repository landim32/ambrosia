using Ambrosia.API.Extensions;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Common;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Images;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosia.API.Controllers;

/// <summary>
/// Imagens da vendedora no storage privado (feature 004, HU 003). O dono vem <b>somente</b> do claim
/// <c>sub</c>: nenhum DTO transporta <c>userId</c>, e imagem de outro usuário responde 404 com a mesma
/// mensagem de "não existe" (FR-014, SC-008).
/// <para>
/// O binário nunca passa pela API no caminho do cliente: <c>POST /images/uploads</c> devolve uma URL
/// pré-assinada, o navegador faz o <c>PUT</c> direto no storage, e <c>.../confirm</c> confere tamanho,
/// tipo e hash antes de aprovar (FR-003, FR-006). A leitura também é direta, por URL assinada gerada a
/// cada resposta (FR-012).
/// </para>
/// </summary>
[Route("images")]
[Authorize]
public class ImagesController : ApiControllerBase
{
    /// <summary>
    /// Teto do corpo de <c>POST /images</c>: <c>MaxUploadBytes</c> padrão (10 MiB) mais folga de 64 KB
    /// para o envelope multipart. Passar por <c>IOptions</c> num filtro não é possível porque o
    /// atributo exige constante de compilação; o serviço revalida o limite real lido da configuração
    /// (T046), então o atributo só evita um corpo absurdamente grande no Kestrel. O gateway já aceita
    /// até 20 MB (research §11).
    /// </summary>
    private const long MaxRequestBytes = 10_485_760 + 65_536;

    private readonly IImageService _imageService;

    public ImagesController(IImageService imageService)
    {
        _imageService = imageService;
    }

    /// <summary>
    /// Declara um envio pelo cliente e devolve a URL pré-assinada onde subir o arquivo (FR-003).
    /// 201 quando há envio a fazer — inclusive quando um envio pendente do mesmo conteúdo é
    /// reaproveitado com URL nova — e 200 quando aquele conteúdo já está disponível (FR-011).
    /// </summary>
    /// <param name="info">Tipo, tamanho e SHA-256 do arquivo local.</param>
    /// <response code="201">Envio criado ou reaproveitado: <c>uploadUrl</c> pronta para o PUT.</response>
    /// <response code="200">Já existe imagem disponível com este conteúdo para o usuário.</response>
    /// <response code="400">Tipo fora da lista, tamanho inválido ou hash mal formado.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("uploads")]
    [ProducesResponseType(typeof(ImageUploadResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ImageUploadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RequestUpload([FromBody] ImageUploadInsertInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            var (result, created) = await _imageService.RequestUploadAsync(User.GetUserId(), info);

            return created ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
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
    /// Confirma que o binário chegou ao storage e o aprova (FR-006, FR-007): compara o tamanho do
    /// objeto, o tipo real pelo conteúdo e o SHA-256 com o que foi declarado. Devolver a mesma
    /// confirmação duas vezes é idempotente e só troca a URL assinada.
    /// </summary>
    /// <param name="imageId">Identificador da imagem devolvido pela declaração.</param>
    /// <response code="200">Imagem disponível, com URL de leitura.</response>
    /// <response code="400">Não enviado, tamanho/tipo/hash divergente, ou envio já recusado.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Imagem inexistente ou de outro usuário.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("uploads/{imageId:long}/confirm")]
    [ProducesResponseType(typeof(ImageInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ConfirmUpload(long imageId)
    {
        try
        {
            return Ok(await _imageService.ConfirmUploadAsync(User.GetUserId(), imageId));
        }
        catch (ImageRejectedException ex)
        {
            return ImageRejectedError(ex);
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
    /// Consulta uma imagem do usuário (FR-012, FR-015): os metadados e, quando ela está disponível, uma
    /// URL pré-assinada <b>nova</b> — o mesmo id consultado duas vezes seguidas devolve duas URLs
    /// diferentes.
    /// </summary>
    /// <param name="imageId">Identificador da imagem.</param>
    /// <response code="200">Imagem; <c>url</c> só vem quando <c>status = available</c>.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Imagem inexistente ou de outro usuário (mesma mensagem nos dois casos).</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("{imageId:long}")]
    [ProducesResponseType(typeof(ImageInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetById(long imageId)
    {
        try
        {
            return Ok(await _imageService.GetByIdAsync(User.GetUserId(), imageId));
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
    /// Grava uma imagem passando pelo servidor (FR-008 a FR-010): o arquivo é validado pelo conteúdo,
    /// tem a orientação corrigida, perde EXIF/IPTC/XMP e é re-codificado antes de subir. É o caminho de
    /// quem não pode fazer um <c>PUT</c> direto ao storage (foto baixada de uma conversa).
    /// </summary>
    /// <param name="file">Arquivo no campo <c>file</c> do multipart.</param>
    /// <response code="201">Imagem gravada e disponível.</response>
    /// <response code="200">O mesmo conteúdo final já estava disponível para o usuário.</response>
    /// <response code="400">Arquivo ausente ou vazio, tipo não aceito ou acima do limite.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="413">Corpo acima do limite da requisição.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType(typeof(ImageInfo), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ImageInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StoreFromServer(IFormFile? file)
    {
        try
        {
            if (file is null || file.Length == 0)
            {
                return BadRequest(new ErrorResult
                {
                    Sucesso = false,
                    Mensagem = InvalidDataMessage,
                    Erros =
                    [
                        new FieldError
                        {
                            Campo = "file",
                            Mensagem = file is null
                                ? "O campo file é obrigatório."
                                : "O campo file não pode ser um arquivo vazio."
                        }
                    ]
                });
            }

            await using var stream = file.OpenReadStream();

            var (image, created) = await _imageService.StoreFromServerAsync(
                User.GetUserId(), stream, ImageSource.Server);

            return created
                ? StatusCode(StatusCodes.Status201Created, image)
                : Ok(image);
        }
        catch (ImageRejectedException ex)
        {
            return ImageRejectedError(ex);
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
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
    /// Exclui uma imagem definitivamente (FR-016): apaga o objeto no storage <b>e</b> o metadado. Vale
    /// para qualquer status. A URL de leitura assinada antes da exclusão passa a responder 404
    /// (<c>NoSuchKey</c>) — é assim que o cliente constata que o conteúdo sumiu (critério 10 da HU).
    /// </summary>
    /// <param name="imageId">Identificador da imagem.</param>
    /// <response code="204">Imagem removida.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Imagem inexistente ou de outro usuário; a de outro dono fica intacta.</response>
    /// <response code="503">Armazenamento de imagens indisponível.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpDelete("{imageId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Delete(long imageId)
    {
        try
        {
            await _imageService.DeleteAsync(User.GetUserId(), imageId);

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
