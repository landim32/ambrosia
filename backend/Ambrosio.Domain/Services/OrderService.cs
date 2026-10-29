using Ambrosio.Domain.Common;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Orders;
using Ambrosio.Infra.Interfaces.Repository;
using FluentValidation;
using FluentValidation.Results;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Regras de pedido (FR-012 a FR-019, FR-023). Duas invariantes estruturais: o pedido só pode ser
/// vinculado a um cliente do próprio usuário (busca por <c>(customerId, userId)</c> → 404 também
/// quando o cliente é de outra vendedora) e a duplicidade é checada pelo quádruplo
/// <c>(usuário, número, data, tipo)</c> antes da inserção, com o índice único do banco fechando a
/// corrida (research §11).
/// </summary>
public class OrderService : IOrderService
{
    private const string ORDER_NOT_FOUND = "Pedido não encontrado.";
    private const string CUSTOMER_NOT_FOUND = "Cliente não encontrado.";
    private const string TOTAL_FIELD = "total";

    private readonly IOrderRepository<Order> _orders;
    private readonly ICustomerRepository<Customer> _customers;

    /// <summary>
    /// Avatar da cliente na tela do pedido (feature 009, FR-016: "todos os lugares onde hoje aparecem
    /// as iniciais"). Só o detalhe do pedido pede a foto — a listagem de pedidos não mostra avatar.
    /// </summary>
    private readonly ICustomerPhotoService _photos;

    private readonly IValidator<OrderInsertInfo> _insertValidator;
    private readonly IValidator<OrderUpdateInfo> _updateValidator;
    private readonly IValidator<OrderListQuery> _listValidator;
    private readonly IClock _clock;

    public OrderService(
        IOrderRepository<Order> orders,
        ICustomerRepository<Customer> customers,
        ICustomerPhotoService photos,
        IValidator<OrderInsertInfo> insertValidator,
        IValidator<OrderUpdateInfo> updateValidator,
        IValidator<OrderListQuery> listValidator,
        IClock clock)
    {
        _orders = orders;
        _customers = customers;
        _photos = photos;
        _insertValidator = insertValidator;
        _updateValidator = updateValidator;
        _listValidator = listValidator;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<PagedResult<OrderInfo>> ListAsync(long userId, OrderListQuery query)
    {
        await ThrowIfInvalid(_listValidator, query);

        var (items, totalCount) = await _orders.ListAsync(userId, query);

        return new PagedResult<OrderInfo>
        {
            Items = items.Select(o => MapToInfo(o, o.Customer)).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };
    }

    /// <inheritdoc />
    public async Task<OrderDetailInfo> GetByIdAsync(long userId, long orderId)
    {
        var order = await GetOwnedOrThrowAsync(orderId, userId);

        var detail = MapToDetail(order);

        // FR-016: onde hoje há iniciais, passa a haver o círculo da cliente — inclusive na tela de
        // pedido. Sem perfil, `profilePhoto` continua null e o avatar cai nas iniciais (US3-5). O
        // original não é assinado aqui: abrir a foto inteira é comportamento do perfil, não do pedido.
        if (detail.Customer.Id != 0)
        {
            var profiles = await _photos.GetProfilesAsync(
                userId, [detail.Customer.Id], includeOriginalUrl: false);

            detail.Customer.ProfilePhoto = profiles.GetValueOrDefault(detail.Customer.Id);
        }

        return detail;
    }

    /// <inheritdoc />
    public async Task<OrderDetailInfo> CreateAsync(long userId, OrderInsertInfo info)
    {
        var customer = await ValidateNewOrderAsync(userId, info);

        var now = _clock.UtcNow;
        var order = new Order
        {
            // FR-021: o dono vem do token, nunca do corpo da requisição.
            UserId = userId,
            CustomerId = customer.Id,
            OrderNumber = Trim(info.OrderNumber),
            OrderDate = info.OrderDate,
            Kind = info.Kind,
            Channel = Trim(info.Channel),
            Origin = info.Origin,
            Status = Trim(info.Status),
            Total = info.Total,
            OriginalSaleOn = info.OriginalSaleOn,
            CancelledOn = info.CancelledOn,
            ExternalId = Trim(info.ExternalId),
            CreatedAt = now,
            UpdatedAt = now,
            Customer = customer
        };

        // FR-018: a numeração das linhas é decisão do agregado.
        foreach (var itemInfo in info.Items)
        {
            order.AddItem(new OrderItem
            {
                ProductName = Trim(itemInfo.ProductName),
                SkuRef = Trim(itemInfo.SkuRef),
                Size = Trim(itemInfo.Size),
                Price = itemInfo.Price,
                ImageUrl = Trim(itemInfo.ImageUrl)
            });
        }

        var saved = await _orders.InsertAsync(order);

        return MapToDetail(saved);
    }

    /// <inheritdoc />
    public async Task<OrderDetailInfo> UpdateAsync(long userId, long orderId, OrderUpdateInfo info)
    {
        var order = await ValidateOrderUpdateAsync(userId, orderId, info);

        order.UpdateStatusAndTotal(info.Status, info.Total, _clock.UtcNow);

        // T074a / FR-023: editar à mão um pedido importado tira dele a impressão digital do conteúdo.
        // Sem isso a reimportação seguinte veria o mesmo hash, classificaria o pedido como "inalterado"
        // e a origem nunca voltaria a prevalecer — a edição da vendedora ficaria escondida para sempre.
        // Com hash nulo, a próxima passada aplica os campos da origem e o relatório mostra "atualizado"
        // com os campos que mudaram (quickstart §4 item 5).
        if (order.Source == OrderSource.MaisVendas)
        {
            order.SourceHash = null;
        }

        var saved = await _orders.UpdateAsync(order);

        return MapToDetail(saved);
    }

    /// <summary>
    /// Não existe e é de outro usuário respondem idêntico (FR-022, SC-002): o 404 não revela se o
    /// pedido existe.
    /// </summary>
    private async Task<Order> GetOwnedOrThrowAsync(long orderId, long userId)
    {
        var order = await _orders.GetByIdAsync(orderId, userId);

        return order ?? throw new NotFoundException(ORDER_NOT_FOUND);
    }

    private static OrderInfo MapToInfo(Order order, Customer? customer) => new()
    {
        Id = order.Id,
        CustomerId = order.CustomerId,
        CustomerName = customer?.Name ?? string.Empty,
        OrderNumber = order.OrderNumber,
        OrderDate = order.OrderDate,
        Kind = order.Kind,
        Channel = order.Channel,
        Origin = order.Origin,
        Status = order.Status,
        Total = order.Total,
        Source = order.Source,
        ItemsCount = order.Items.Count,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt
    };

    /// <inheritdoc />
    public async Task<string> ValidateCreateAsync(long userId, OrderInsertInfo info)
    {
        var customer = await ValidateNewOrderAsync(userId, info);

        return customer.Name;
    }

    /// <inheritdoc />
    public async Task<OrderDetailInfo> ValidateUpdateAsync(long userId, long orderId, OrderUpdateInfo info) =>
        MapToDetail(await ValidateOrderUpdateAsync(userId, orderId, info));

    /// <summary>Validator, cliente do usuário e duplicidade — compartilhado pela gravação e pela prévia.</summary>
    private async Task<Customer> ValidateNewOrderAsync(long userId, OrderInsertInfo info)
    {
        await ThrowIfInvalid(_insertValidator, info);

        // FR-023: o cliente precisa existir E ser do usuário. GetByIdAsync já filtra por userId,
        // então cliente de outra vendedora cai na mesma 404 (SC-002, sem revelar existência).
        var customer = await _customers.GetByIdAsync(info.CustomerId, userId);

        if (customer is null)
        {
            throw new NotFoundException(CUSTOMER_NOT_FOUND);
        }

        // FR-014: verificação prévia dá a mensagem clara; o índice único ux_orders_user_number_date_kind
        // resolve a corrida entre duas sessões (research §11).
        if (await _orders.ExistsAsync(userId, info.OrderNumber.Trim(), info.OrderDate, info.Kind))
        {
            throw new DuplicateException("Já existe um pedido com este número, data e tipo.");
        }

        return customer;
    }

    /// <summary>Validator e sinal do total pelo tipo gravado — compartilhado pela gravação e pela prévia.</summary>
    private async Task<Order> ValidateOrderUpdateAsync(long userId, long orderId, OrderUpdateInfo info)
    {
        await ThrowIfInvalid(_updateValidator, info);

        var order = await GetOwnedOrThrowAsync(orderId, userId);

        // O sinal é checado contra o Kind GRAVADO (kind é imutável — data-model): a resposta usa
        // a mesma mensagem do validator da inserção, apontando o campo `total` (FR-024).
        if (!Order.IsTotalSignValid(order.Kind, info.Total))
        {
            throw new ValidationException(
                [new ValidationFailure(TOTAL_FIELD, OrderTotalSignRule.ErrorMessage(order.Kind))]);
        }

        return order;
    }

    internal static OrderDetailInfo MapToDetail(Order order) => new()
    {
        Id = order.Id,
        CustomerId = order.CustomerId,
        CustomerName = order.Customer?.Name ?? string.Empty,
        OrderNumber = order.OrderNumber,
        OrderDate = order.OrderDate,
        Kind = order.Kind,
        Channel = order.Channel,
        Origin = order.Origin,
        Status = order.Status,
        Total = order.Total,
        Source = order.Source,
        ItemsCount = order.Items.Count,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt,
        OriginalSaleOn = order.OriginalSaleOn,
        CancelledOn = order.CancelledOn,
        ExternalId = order.ExternalId,
        CapturedAt = order.CapturedAt,
        // SC-005: CustomerInfo não tem campo para o CPF completo — a decifração nem acontece aqui.
        Customer = MapCustomer(order.Customer),
        Items = order.Items
            .OrderBy(i => i.LineNo)
            .Select(i => new OrderItemInfo
            {
                Id = i.Id,
                LineNo = i.LineNo,
                ProductName = i.ProductName,
                SkuRef = i.SkuRef,
                Size = i.Size,
                Price = i.Price,
                ImageUrl = i.ImageUrl,
                ImageId = i.ImageId
            })
            .ToList()
    };

    private static CustomerInfo MapCustomer(Customer? customer) => customer is null
        ? new CustomerInfo()
        : new CustomerInfo
        {
            Id = customer.Id,
            Name = customer.Name,
            CpfMasked = customer.CpfMasked,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt
        };

    /// <summary>Textos vão despidos de espaços nas pontas (a coluna tem limite e o índice compara exato).</summary>
    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task ThrowIfInvalid<T>(IValidator<T> validator, T target)
    {
        var result = await validator.ValidateAsync(target);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}
