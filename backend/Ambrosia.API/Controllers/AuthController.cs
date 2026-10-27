using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Auth;
using Ambrosia.DTO.Common;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosia.API.Controllers;

/// <summary>
/// Emissão de token do Ambrosia: cadastro, login com senha e Google. É o único grupo público
/// da API (research §1, §2): as demais rotas exigem <c>Authorization: Bearer</c>, e não existe
/// mais nenhum atalho sem credencial em ambiente nenhum (constituição §VI).
/// </summary>
[Route("auth")]
[AllowAnonymous]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService) => _authService = authService;

    /// <summary>
    /// Cria a conta com nome, e-mail e senha e já inicia a sessão (FR-001): a senha é guardada
    /// apenas como hash PBKDF2 e nunca volta em resposta alguma (FR-005). O e-mail é normalizado
    /// e único; a rota é anônima como as demais de <c>/auth/*</c>.
    /// </summary>
    /// <param name="info">Nome, e-mail e senha informados.</param>
    /// <response code="201">Conta criada e sessão iniciada (token + perfil).</response>
    /// <response code="400">Nome, e-mail ou senha fora das regras de formato.</response>
    /// <response code="409">E-mail já cadastrado (com orientação ao Google quando for conta dele).</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("register")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            // Sem Location: o recurso criado é a sessão, não um /users/{id} navegável (research §3).
            return StatusCode(StatusCodes.Status201Created, await _authService.RegisterAsync(info));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (DuplicateException ex)
        {
            return ConflictError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Entra com e-mail e senha (FR-002). Devolve o mesmo token do login Google (HS256, 8 h,
    /// claims <c>sub</c>/<c>email</c>/<c>name</c> — FR-007).
    /// </summary>
    /// <remarks>
    /// E-mail inexistente, senha incorreta e conta sem senha (só Google) respondem o MESMO 401
    /// com a mesma mensagem, em tempo equivalente (FR-008, SC-006): a rota não revela quais
    /// e-mail existem no cadastro.
    /// </remarks>
    /// <param name="info">E-mail e senha informados.</param>
    /// <response code="200">Token do Ambrosia.</response>
    /// <response code="400">E-mail ou senha ausentes / e-mail mal formado.</response>
    /// <response code="401">Credencial recusada (mensagem única).</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            return Ok(await _authService.LoginWithPasswordAsync(info));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (InvalidCredentialsException ex)
        {
            return InvalidCredentialsError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Troca um ID token do Google por um JWT do Ambrosia. No primeiro acesso cria o usuário pelo
    /// e-mail verificado; nos acessos seguintes reutiliza o usuário e atualiza nome/subject.
    /// </summary>
    /// <remarks>
    /// Reutilizando uma conta que tinha senha, a senha é <b>removida</b> neste login (FR-009): o
    /// Google comprova a posse do e-mail, e a partir daí só ele entra. Sessões já emitidas por
    /// senha continuam válidas até expirar.
    /// </remarks>
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
}
