using Ambrosia.Domain.Common;
using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Orders;
using FluentValidation;

namespace Ambrosia.Domain.Validators;

/// <summary>
/// Validação de <see cref="OrderInsertInfo"/> (FR-012 a FR-015), com "hoje" vindo de
/// <see cref="IClock"/> para que a regra de data não futura seja testável (tasks.md, ajuste ao
/// research §6). As regras que exigem banco — cliente pertencer ao usuário e duplicidade
/// (número, data, tipo) — ficam no serviço (research §8).
/// </summary>
public class OrderInsertInfoValidator : AbstractValidator<OrderInsertInfo>
{
    private readonly IClock _clock;

    public OrderInsertInfoValidator(IClock clock)
    {
        _clock = clock;

        RuleFor(x => x.CustomerId)
            .GreaterThan(0)
            .WithMessage("O cliente é obrigatório.");

        RuleFor(x => x.OrderNumber)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O número do pedido é obrigatório.")
            .MaximumLength(60)
                .WithMessage("O número do pedido deve ter no máximo 60 caracteres.");

        // FR-012: obrigatória e nunca futura. Um DateOnly ausente no JSON chega como 0001-01-01
        // (default estrutural) e precisa ser recusado como "não informada", não como data antiga.
        RuleFor(x => x.OrderDate)
            .Cascade(CascadeMode.Stop)
            .Must(date => date != default)
                .WithMessage("A data do pedido é obrigatória.")
            .Must((info, date) => date <= _clock.Today)
                .WithMessage("A data do pedido não pode ser futura.");

        RuleFor(x => x.Kind)
            .IsInEnum()
            .WithMessage("O tipo do pedido é inválido.");

        RuleFor(x => x.Origin)
            .Must(origin => origin is null || Enum.IsDefined(typeof(OrderOrigin), origin.Value))
            .WithMessage("A origem do pedido é inválida.");

        // FR-013: o sinal exigido depende do tipo. A regra e a mensagem vivem em
        // OrderTotalSignRule, compartilhadas com Order.IsTotalSignValid e com o serviço na
        // atualização (T079/T082) — uma única fonte para não divergir (FR-024).
        RuleFor(x => x.Total)
            .Must((info, total) => OrderTotalSignRule.IsValid(info.Kind, total))
                .WithMessage((info, _) => OrderTotalSignRule.ErrorMessage(info.Kind));

        RuleFor(x => x.Channel)
            .MaximumLength(60)
            .WithMessage("O canal deve ter no máximo 60 caracteres.");

        RuleFor(x => x.Status)
            .MaximumLength(60)
            .WithMessage("O status deve ter no máximo 60 caracteres.");

        RuleFor(x => x.ExternalId)
            .MaximumLength(60)
            .WithMessage("O identificador externo deve ter no máximo 60 caracteres.");

        // As duas datas são opcionais para qualquer tipo (data-model); o alerta é apenas para a
        // ordem invertida, apontando o campo que chegou depois da outra.
        RuleFor(x => x.CancelledOn)
            .Must((info, cancelledOn) => !IsInvertedDateRange(info, cancelledOn))
            .WithMessage("A data do cancelamento não pode ser anterior à data da venda.");

        // FR-015.
        RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("O pedido deve ter ao menos um item.");

        // Validação das linhas (T080): gera propertyName "Items[0].ImageUrl", que o controller
        // converte para "items[0].imageUrl" no ErrorResult (ApiControllerBase).
        RuleForEach(x => x.Items)
            .SetValidator(new OrderItemInsertInfoValidator());
    }

    private static bool IsInvertedDateRange(OrderInsertInfo info, DateOnly? cancelledOn) =>
        info.OriginalSaleOn is { } original
        && cancelledOn is { } cancelled
        && cancelled < original;
}
