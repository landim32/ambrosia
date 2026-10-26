using Ambrosia.API.Extensions;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Common;
using Ambrosia.DTO.Customers;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosia.API.Controllers;

/// <summary>
/// Cadastro e consulta de clientes da vendedora autenticada (FR-006 a FR-011). O
/// <c>userId</c> vem <b>somente</b> do claim <c>sub</c>: nenhum DTO de entrada transporta o dono
/// do registro, e recurso de outro usuário responde 404 (FR-021, FR-022).
/// </summary>
[Route("customers")]
[Authorize]
public class CustomersController : ApiControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    /// <summary>
    /// Lista os clientes do usuário, com busca por trecho de nome e CPF apenas mascarado
    /// (SC-005). Página além do fim devolve <c>items: []</c> com <c>totalCount</c> correto.
    /// </summary>
    /// <param name="query">Filtro e paginação (<c>search</c>, <c>page</c> ≥ 1, <c>pageSize</c> 1-100).</param>
    /// <response code="200">Página de clientes.</response>
    /// <response code="400">Paginação inválida.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CustomerInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] CustomerListQuery query)
    {
        try
        {
            return Ok(await _customerService.ListAsync(User.GetUserId(), query));
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
    /// Detalhe do cliente: CPF completo decifrado e resumo de compras (FR-009). Recurso de outro
    /// usuário responde 404 com a mesma mensagem de "não existe" (FR-022).
    /// </summary>
    /// <param name="id">Identificador do cliente.</param>
    /// <response code="200">Cliente com CPF completo e resumo.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente não encontrado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(CustomerDetailInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id)
    {
        try
        {
            return Ok(await _customerService.GetByIdAsync(User.GetUserId(), id));
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

    /// <summary>
    /// Cadastra um cliente (nome + CPF). O CPF é validado, normalizado e armazenado apenas como
    /// hash + cifra + máscara (FR-006, FR-007, FR-011).
    /// </summary>
    /// <param name="info">Nome (2-200) e CPF, com ou sem pontuação.</param>
    /// <response code="201">Cliente criado (Location: /customers/{id}).</response>
    /// <response code="400">Nome ou CPF inválidos.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="409">Já existe cliente com este CPF para o usuário.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost]
    [ProducesResponseType(typeof(CustomerDetailInfo), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CustomerInsertInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            var created = await _customerService.CreateAsync(User.GetUserId(), info);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
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
    /// Atualiza o nome do cliente (FR-009). O CPF é imutável nesta entrega e as colunas de
    /// proteção não são reescritas.
    /// </summary>
    /// <param name="id">Identificador do cliente.</param>
    /// <param name="info">Novo nome (2-200 caracteres).</param>
    /// <response code="200">Cliente atualizado.</response>
    /// <response code="400">Nome inválido.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente não encontrado (inclui cliente de outro usuário).</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(CustomerDetailInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(long id, [FromBody] CustomerUpdateInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            return Ok(await _customerService.UpdateAsync(User.GetUserId(), id, info));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
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
