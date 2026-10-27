using Ambrosio.DTO.Orders;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Validação de <see cref="OrderUpdateInfo"/> (FR-019): status é texto livre com o limite da
/// coluna e o total não pode ser zero. O sinal contra o tipo <b>gravado</b> é checado no serviço /
/// no agregado, porque o validator não conhece o pedido persistido (research §8).
/// </summary>
public class OrderUpdateInfoValidator : AbstractValidator<OrderUpdateInfo>
{
    public OrderUpdateInfoValidator()
    {
        RuleFor(x => x.Status)
            .MaximumLength(60)
            .WithMessage("O status deve ter no máximo 60 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Status));

        RuleFor(x => x.Total)
            .NotEqual(0m)
            .WithMessage("O total não pode ser zero.");
    }
}
