using Ambrosio.API.Extensions;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosio.API.Controllers;

/// <summary>
/// Perfil do usuário autenticado. Todo dado lido aqui vem do claim <c>sub</c> do token: nenhuma
/// rota aceita identificador de outro usuário (FR-021).
/// </summary>
[Route("users")]
[Authorize]
public class UsersController : ApiControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Devolve o próprio perfil — <c>id</c>, <c>email</c>, <c>name</c>, <c>provider</c> e
    /// <c>createdAt</c>. Nenhum campo de senha existe no contrato (FR-003).
    /// </summary>
    /// <response code="200">Perfil do usuário autenticado.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Usuário do token não existe mais.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me()
    {
        try
        {
            return Ok(await _userService.GetByIdAsync(User.GetUserId()));
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
}
