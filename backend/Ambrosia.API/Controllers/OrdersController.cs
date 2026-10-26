using Ambrosia.API.Extensions;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Common;
using Ambrosia.DTO.Orders;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosia.API.Controllers;

/// <summary>
/// Cadastro e consulta de pedidos da vendedora autenticada (FR-012 a FR-019). Espelha
/// <c>CustomersController</c>: o <c>userId</c> vem apenas do claim <c>sub</c>, e um pedido nunca é
/// vinculado a cliente de outra vendedora (FR-023 → 404).
/// </summary>
[Route("orders")]
[Authorize]
public class OrdersController : ApiControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// Lista os pedidos do usuário com filtros e paginação, ordenados por data decrescente
    /// (FR-017). <c>from</c>/<c>to</c> são inclusivos, <c>channel</c> compara por igualdade sem
    /// diferenciar maiúsculas e <c>search</c> varre número do pedido OU nome do cliente.
    /// </summary>
    /// <param name="query">Filtros e paginação (<c>page</c> ≥ 1, <c>pageSize</c> 1-100).</param>
    /// <response code="200">Página de pedidos.</response>
    /// <response code="400">Filtro ou paginação inválidos.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] OrderListQuery query)
    {
        try
        {
            return Ok(await _orderService.ListAsync(User.GetUserId(), query));
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
    /// Detalhe do pedido com o cliente (somente CPF mascarado) e as linhas numeradas
    /// (FR-016, SC-005).
    /// </summary>
    /// <param name="id">Identificador do pedido.</param>
    /// <response code="200">Pedido com cliente e itens.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Pedido não encontrado (inclui pedido de outro usuário).</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(OrderDetailInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id)
    {
        try
        {
            return Ok(await _orderService.GetByIdAsync(User.GetUserId(), id));
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
    /// Cadastra um pedido com itens (FR-012 a FR-015): venda com total positivo, devolução ou
    /// cancelamento com total negativo, ao menos uma linha e data não futura. O tipo
    /// <c>kind</c> é imutável depois de criado — cancelar é criar um pedido novo negativo, como no
    /// +Vendas (SC-006).
    /// </summary>
    /// <param name="info">Pedido e linhas.</param>
    /// <response code="201">Pedido criado (Location: /orders/{id}).</response>
    /// <response code="400">Total com sinal incompatível, sem itens, data futura ou campos inválidos.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Cliente não encontrado (inclui cliente de outro usuário).</response>
    /// <response code="409">Já existe pedido com este número, data e tipo.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDetailInfo), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] OrderInsertInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            var created = await _orderService.CreateAsync(User.GetUserId(), info);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
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
    /// Atualiza status e total do pedido (FR-019). O total é revalidado contra o tipo gravado:
    /// um pedido de venda não pode passar a ter total negativo.
    /// </summary>
    /// <param name="id">Identificador do pedido.</param>
    /// <param name="info">Novo status (opcional) e novo total.</param>
    /// <response code="200">Pedido atualizado.</response>
    /// <response code="400">Total zero ou com sinal incompatível com o tipo.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Pedido não encontrado (inclui pedido de outro usuário).</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(OrderDetailInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(long id, [FromBody] OrderUpdateInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            return Ok(await _orderService.UpdateAsync(User.GetUserId(), id, info));
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
