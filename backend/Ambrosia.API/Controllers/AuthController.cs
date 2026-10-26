using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Auth;
using Ambrosia.DTO.Common;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosia.API.Controllers;

/// <summary>
/// Emissão de token do Ambrosia. A única rota pública da API (research §1, §2): as demais exigem
/// <c>Authorization: Bearer</c>.
/// </summary>
[Route("auth")]
[AllowAnonymous]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly IHostEnvironment _environment;

    public AuthController(IAuthService authService, IHostEnvironment environment)
    {
        _authService = authService;
        _environment = environment;
    }

    /// <summary>
    /// Troca um ID token do Google por um JWT do Ambrosia. No primeiro acesso cria o usuário pelo
    /// e-mail verificado; nos acessos seguintes reutiliza o usuário e atualiza nome/subject.
    /// </summary>
    /// <param name="info">ID token obtido pelo cliente no Google.</param>
    /// <response code="200">Token do Ambrosia.</response>
    /// <response code="400">ID token ausente/inválido ou sem e-mail verificado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("google")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Google([FromBody] GoogleLoginInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            return Ok(await _authService.LoginWithGoogleAsync(info));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// <b>Somente em Development.</b> Emite um JWT sem passar pelo Google, para o quickstart e
    /// para os testes HTTP. Fora desse ambiente responde 404 (research §2, constituição §VI).
    /// </summary>
    /// <param name="info">E-mail e nome do usuário de desenvolvimento.</param>
    /// <response code="200">Token do Ambrosia.</response>
    /// <response code="400">E-mail/nome inválidos.</response>
    /// <response code="404">Indisponível neste ambiente.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("dev-login")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DevLogin([FromBody] DevLoginInfo info)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            return Ok(await _authService.DevLoginAsync(info));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
}
