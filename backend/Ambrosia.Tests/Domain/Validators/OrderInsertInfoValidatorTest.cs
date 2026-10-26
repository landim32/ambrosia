using Ambrosia.Domain.Validators;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Orders;
using Ambrosia.Tests.TestHelpers;

namespace Ambrosia.Tests.Domain.Validators;

/// <summary>
/// T072 [US3]: validação de pedido com <c>IClock</c> fixo em 2026-10-25 — sinal do total por tipo
/// (FR-013), ao menos um item (FR-015), data não futura (FR-012) e limites de tamanho das colunas
/// (data-model). Inclui os casos de <see cref="OrderUpdateInfoValidator"/> e
/// <see cref="OrderListQueryValidator"/>, conforme a tarefa.
/// </summary>
public class OrderInsertInfoValidatorTest
{
    private static readonly FakeClock Clock =
        new(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified));

    private readonly OrderInsertInfoValidator _sut = new(Clock);

    private static OrderItemInsertInfo Item(
        string? productName = "Calça Inez",
        string? skuRef = "42CA500UIE_12182",
        string? size = "38",
        decimal? price = 399.90m,
        string? imageUrl = null) => new()
    {
        ProductName = productName,
        SkuRef = skuRef,
        Size = size,
        Price = price,
        ImageUrl = imageUrl
    };

    private static OrderInsertInfo Valid(
        OrderKind kind = OrderKind.Sale,
        decimal total = 589.80m,
        DateOnly? orderDate = null,
        List<OrderItemInsertInfo>? items = null) => new()
    {
        CustomerId = 11,
        OrderNumber = "00004751",
        OrderDate = orderDate ?? new DateOnly(2026, 10, 24),
        Kind = kind,
        Channel = "Linx",
        Origin = OrderOrigin.Cota,
        Status = "Faturado",
        Total = total,
        Items = items ??
        [
            Item(),
            Item(productName: "Blusa Lia", skuRef: "41BL200AAA_00001", size: "P", price: 189.90m)
        ]
    };

    [Fact]
    public async Task Validate_ShouldSucceed_WithTheQuickstartSale()
    {
        // Act: a venda de 2 itens do Cenário 3 do quickstart.
        var result = await _sut.ValidateAsync(Valid());

        // Assert
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public async Task Validate_ShouldSucceed_WithNegativeCancellationAndBothDates()
    {
        // Arrange: SC-006 — cancelamento com total negativo e as datas da venda original.
        var info = Valid(OrderKind.Cancellation, -589.80m, new DateOnly(2026, 10, 25));
        info.OriginalSaleOn = new DateOnly(2026, 10, 24);
        info.CancelledOn = new DateOnly(2026, 10, 25);
        info.Status = "Cancelado";
        info.Items = [Item(price: -399.90m)];

        // Act
        var result = await _sut.ValidateAsync(info);

        // Assert
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_ShouldRequireCustomerId_WhenItIsNotPositive(long customerId)
    {
        // Arrange: FR-012 — a posse do cliente é verificada no serviço; aqui só o formato.
        var info = Valid();
        info.CustomerId = customerId;

        // Act
        var result = await _sut.ValidateAsync(info);

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "CustomerId");
        Assert.Equal("O cliente é obrigatório.", error.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_ShouldRequireOrderNumber_WhenItIsMissing(string? orderNumber)
    {
        // Arrange
        var info = Valid();
        info.OrderNumber = orderNumber!;

        // Act
        var result = await _sut.ValidateAsync(info);

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "OrderNumber");
        Assert.Equal("O número do pedido é obrigatório.", error.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRejectOrderNumberOver60_WhenLongerThanTheColumn()
    {
        // orders.order_number é varchar(60) (data-model).
        var info = Valid();
        info.OrderNumber = new string('9', 61);

        var result = await _sut.ValidateAsync(info);

        Assert.Equal("O número do pedido deve ter no máximo 60 caracteres.",
            Assert.Single(result.Errors, e => e.PropertyName == "OrderNumber").ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRequireOrderDate_WhenTheClientDidNotSendIt()
    {
        // Arrange: DateOnly ausente no JSON vira 0001-01-01 (default), que precisa ser recusado
        // como "não informada" (FR-012).
        var info = Valid();
        info.OrderDate = default;

        // Act
        var result = await _sut.ValidateAsync(info);

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "OrderDate");
        Assert.Equal("A data do pedido é obrigatória.", error.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRejectFutureDate_WhenOrderDateIsAfterToday()
    {
        // Arrange: "hoje" é 2026-10-25 pelo relógio injetado; 2026-10-26 é futuro (FR-012).
        var info = Valid(orderDate: new DateOnly(2026, 10, 26));

        // Act
        var result = await _sut.ValidateAsync(info);

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "OrderDate");
        Assert.Equal("A data do pedido não pode ser futura.", error.ErrorMessage);
    }

    [Theory]
    [InlineData(2026, 10, 25)]
    [InlineData(2020, 1, 1)]
    public async Task Validate_ShouldAcceptTodayAndPastDates_WhenDateIsNotFuture(int year, int month, int day)
    {
        // Act
        var result = await _sut.ValidateAsync(Valid(orderDate: new DateOnly(year, month, day)));

        // Assert
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public async Task Validate_ShouldRejectKindOutsideTheEnum_WhenValueIsUnknown()
    {
        // research §7: kind é conjunto fechado (Sale=1, Return=2, Cancellation=3).
        var info = Valid();
        info.Kind = (OrderKind)99;

        var result = await _sut.ValidateAsync(info);

        Assert.Equal("O tipo do pedido é inválido.",
            Assert.Single(result.Errors, e => e.PropertyName == "Kind").ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRejectOriginOutsideTheEnum_WhenValueIsUnknown()
    {
        var info = Valid();
        info.Origin = (OrderOrigin?)77;

        var result = await _sut.ValidateAsync(info);

        Assert.Equal("A origem do pedido é inválida.",
            Assert.Single(result.Errors, e => e.PropertyName == "Origin").ErrorMessage);
    }

    [Theory]
    [InlineData(0.00)]
    [InlineData(-0.01)]
    [InlineData(-589.80)]
    public async Task Validate_ShouldRequirePositiveTotal_WhenKindIsSale(decimal total)
    {
        // Act: FR-013.
        var result = await _sut.ValidateAsync(Valid(OrderKind.Sale, total));

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "Total");
        Assert.Equal("O total de uma venda deve ser positivo.", error.ErrorMessage);
    }

    [Theory]
    [InlineData(OrderKind.Return, 589.80)]
    [InlineData(OrderKind.Cancellation, 589.80)]
    [InlineData(OrderKind.Return, 0.00)]
    [InlineData(OrderKind.Cancellation, 0.00)]
    public async Task Validate_ShouldRequireNegativeTotal_WhenKindIsReturnOrCancellation(
        OrderKind kind, decimal total)
    {
        // Act: FR-013.
        var result = await _sut.ValidateAsync(Valid(kind, total));

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "Total");
        Assert.Equal("O total de uma devolução ou cancelamento deve ser negativo.", error.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRequireAtLeastOneItem_WhenItemsIsEmpty()
    {
        // Act: FR-015.
        var result = await _sut.ValidateAsync(Valid(items: []));

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "Items");
        Assert.Equal("O pedido deve ter ao menos um item.", error.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRequireAtLeastOneItem_WhenItemsIsNull()
    {
        // Arrange: corpo JSON sem a propriedade items.
        var info = Valid();
        info.Items = null!;

        // Act
        var result = await _sut.ValidateAsync(info);

        // Assert
        Assert.Equal("O pedido deve ter ao menos um item.",
            Assert.Single(result.Errors, e => e.PropertyName == "Items").ErrorMessage);
    }

    [Theory]
    [InlineData("Channel")]
    [InlineData("Status")]
    [InlineData("ExternalId")]
    public async Task Validate_ShouldRejectTextOver60_WhenColumnIsVarchar60(string field)
    {
        // data-model: channel, status e external_id são varchar(60).
        var info = Valid();
        var longValue = new string('x', 61);

        switch (field)
        {
            case "Channel":
                info.Channel = longValue;
                break;
            case "Status":
                info.Status = longValue;
                break;
            default:
                info.ExternalId = longValue;
                break;
        }

        var result = await _sut.ValidateAsync(info);

        Assert.Contains(result.Errors, e =>
            e.PropertyName == field && e.ErrorMessage.Contains("60", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_ShouldAcceptAChannelOutsideTheKnownList_WhenChannelIsFreeText()
    {
        // Edge Cases da spec + Complexity Tracking do plan.md: canais novos do +Vendas são
        // aceitos sem perda de informação (SC-006) — por isso channel é varchar, não enum.
        var info = Valid();
        info.Channel = "Outlet";

        var result = await _sut.ValidateAsync(info);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_ShouldRejectCancelledOnBeforeOriginalSaleOn_WhenDatesAreInverted()
    {
        // Arrange: T072 — o alerta só existe quando as duas datas vieram invertidas.
        var info = Valid(OrderKind.Cancellation, -589.80m, new DateOnly(2026, 10, 25));
        info.OriginalSaleOn = new DateOnly(2026, 10, 20);
        info.CancelledOn = new DateOnly(2026, 10, 19);

        // Act
        var result = await _sut.ValidateAsync(info);

        // Assert
        Assert.Contains(result.Errors, e =>
            e.PropertyName == "CancelledOn"
            && e.ErrorMessage == "A data do cancelamento não pode ser anterior à data da venda.");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Validate_ShouldAcceptOneDateAlone_WhenOnlyOneOfTheTwoIsSent(
        bool withOriginalSaleOn, bool withCancelledOn)
    {
        // data-model: as duas datas são opcionais e aceitas para qualquer tipo.
        var info = Valid(OrderKind.Cancellation, -589.80m, new DateOnly(2026, 10, 25));

        if (withOriginalSaleOn)
        {
            info.OriginalSaleOn = new DateOnly(2026, 10, 20);
        }

        if (withCancelledOn)
        {
            info.CancelledOn = new DateOnly(2026, 10, 25);
        }

        var result = await _sut.ValidateAsync(info);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }
}

/// <summary>
/// T072 [US3]: validação de <c>PUT /orders/{id}</c> — status texto livre com máximo 60 e total
/// diferente de zero (FR-019). O sinal contra o <c>kind</c> gravado é regra do serviço/modelo.
/// </summary>
public class OrderUpdateInfoValidatorTest
{
    private readonly OrderUpdateInfoValidator _sut = new();

    [Fact]
    public async Task Validate_ShouldSucceed_WithStatusAndValidTotal()
    {
        var result = await _sut.ValidateAsync(new OrderUpdateInfo { Status = "Faturado", Total = 579.80m });

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public async Task Validate_ShouldAcceptNullStatus_WhenStatusIsNotSent()
    {
        // status é opcional no contrato (openapi.yaml: only `total` é required).
        var result = await _sut.ValidateAsync(new OrderUpdateInfo { Status = null, Total = -10m });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_ShouldRejectStatusOver60_WhenLongerThanTheColumn()
    {
        var result = await _sut.ValidateAsync(
            new OrderUpdateInfo { Status = new string('s', 61), Total = 10m });

        Assert.Equal("O status deve ter no máximo 60 caracteres.",
            Assert.Single(result.Errors, e => e.PropertyName == "Status").ErrorMessage);
    }

    [Theory]
    [InlineData(0.00)]
    [InlineData(0.000)]
    public async Task Validate_ShouldRejectZeroTotal_WhenTotalIsZero(decimal total)
    {
        // FR-013: nenhum tipo de pedido tem total zero.
        var result = await _sut.ValidateAsync(new OrderUpdateInfo { Status = "X", Total = total });

        Assert.Equal("O total não pode ser zero.",
            Assert.Single(result.Errors, e => e.PropertyName == "Total").ErrorMessage);
    }
}

/// <summary>
/// T072 [US3]: limites da listagem (FR-017, research §9) — paginação e intervalo de datas.
/// </summary>
public class OrderListQueryValidatorTest
{
    private readonly OrderListQueryValidator _sut = new();

    [Fact]
    public async Task Validate_ShouldSucceed_WithDefaults()
    {
        var result = await _sut.ValidateAsync(new OrderListQuery());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Validate_ShouldRejectPageBelowOne_WhenPageIsInvalid(int page)
    {
        var result = await _sut.ValidateAsync(new OrderListQuery { Page = page });

        Assert.Equal("A página deve ser maior ou igual a 1.",
            Assert.Single(result.Errors, e => e.PropertyName == "Page").ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Validate_ShouldRejectPageSizeOutOfRange_WhenPageSizeIsInvalid(int pageSize)
    {
        var result = await _sut.ValidateAsync(new OrderListQuery { PageSize = pageSize });

        Assert.Equal("O tamanho da página deve estar entre 1 e 100.",
            Assert.Single(result.Errors, e => e.PropertyName == "PageSize").ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRejectInvertedInterval_WhenFromIsAfterTo()
    {
        // Arrange: FR-017 — from/to inclusivos, e from não pode ser depois de to.
        var query = new OrderListQuery
        {
            From = new DateOnly(2026, 10, 31),
            To = new DateOnly(2026, 10, 1)
        };

        // Act
        var result = await _sut.ValidateAsync(query);

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "From");
        Assert.Equal("A data inicial não pode ser maior que a data final.", error.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldAcceptSameDayInterval_WhenFromEqualsTo()
    {
        var day = new DateOnly(2026, 10, 24);

        var result = await _sut.ValidateAsync(new OrderListQuery { From = day, To = day });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_ShouldRejectKindOutsideTheEnum_WhenFilterIsUnknown()
    {
        var result = await _sut.ValidateAsync(new OrderListQuery { Kind = (OrderKind)42 });

        Assert.Equal("O tipo do pedido é inválido.",
            Assert.Single(result.Errors, e => e.PropertyName == "Kind").ErrorMessage);
    }
}

/// <summary>
/// T072 [US3]: validação das linhas do pedido (FR-015) — ao menos um identificador do produto,
/// limites de coluna e URL absoluta http/https para a imagem.
/// </summary>
public class OrderItemInsertInfoValidatorTest
{
    private readonly OrderItemInsertInfoValidator _sut = new();

    private static OrderItemInsertInfo Item(
        string? productName = "Calça Inez",
        string? skuRef = "42CA500UIE_12182",
        string? size = "38",
        decimal? price = 399.90m,
        string? imageUrl = null) => new()
    {
        ProductName = productName,
        SkuRef = skuRef,
        Size = size,
        Price = price,
        ImageUrl = imageUrl
    };

    [Fact]
    public async Task Validate_ShouldSucceed_WithNameAndSku()
    {
        var result = await _sut.ValidateAsync(Item());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("Calça Inez", null)]
    [InlineData(null, "42CA500UIE_12182")]
    public async Task Validate_ShouldAcceptOneIdentifierAlone_WhenTheOtherIsMissing(
        string? productName, string? skuRef)
    {
        // FR-015: basta um dos dois — o +Vendas nem sempre traz nome e SKU juntos.
        var result = await _sut.ValidateAsync(Item(productName: productName, skuRef: skuRef));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public async Task Validate_ShouldRejectItemWithoutAnyIdentifier_WhenNameAndSkuAreEmpty()
    {
        // Act: FR-015.
        var result = await _sut.ValidateAsync(Item(productName: null, skuRef: null));

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "ProductName");
        Assert.Equal("Informe o nome do produto ou a referência (SKU).", error.ErrorMessage);
    }

    [Theory]
    [InlineData("ProductName", 200)]
    [InlineData("SkuRef", 60)]
    [InlineData("Size", 20)]
    public async Task Validate_ShouldRejectOverTheColumnLimit_WhenTextIsTooLong(
        string property, int max)
    {
        // Arrange: varchar(200)/varchar(60)/varchar(20) do data-model.
        var item = Item();
        var tooLong = new string('x', max + 1);

        switch (property)
        {
            case "SkuRef":
                item.SkuRef = tooLong;
                break;
            case "Size":
                item.Size = tooLong;
                break;
            default:
                item.ProductName = tooLong;
                break;
        }

        // Act
        var result = await _sut.ValidateAsync(item);

        // Assert
        Assert.Contains(result.Errors, e =>
            e.PropertyName == property && e.ErrorMessage.Contains(max.ToString(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://cdn.exemplo.com/a.jpg", true)]
    [InlineData("http://cdn.exemplo.com/a.jpg", true)]
    [InlineData("//cdn.exemplo.com/a.jpg", false)]
    [InlineData("cdn.exemplo.com/a.jpg", false)]
    [InlineData("ftp://cdn.exemplo.com/a.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    public async Task Validate_ShouldRequireAbsoluteHttpUrl_WhenImageUrlIsSent(
        string imageUrl, bool expected)
    {
        // Act: T072 — URL absoluta http/https (a coluna é varchar(500)).
        var result = await _sut.ValidateAsync(Item(imageUrl: imageUrl));

        // Assert
        Assert.Equal(expected, result.IsValid);

        if (!expected)
        {
            Assert.Equal("A URL da imagem deve ser absoluta (http ou https).",
                Assert.Single(result.Errors, e => e.PropertyName == "ImageUrl").ErrorMessage);
        }
    }

    [Fact]
    public async Task Validate_ShouldRejectImageUrlOver500_WhenLongerThanTheColumn()
    {
        var result = await _sut.ValidateAsync(
            Item(imageUrl: "https://cdn.exemplo.com/" + new string('i', 500)));

        Assert.Contains("500", Assert.Single(result.Errors, e => e.PropertyName == "ImageUrl").ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldAcceptNullImageUrl_WhenTheOriginHasNoImage()
    {
        var result = await _sut.ValidateAsync(Item(imageUrl: null));

        Assert.True(result.IsValid);
    }
}
