using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Orders;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Limites da listagem de pedidos (FR-017, research §9): paginação 1..100, intervalo de datas
/// coerente e tipo dentro do enum.
/// </summary>
public class OrderListQueryValidator : AbstractValidator<OrderListQuery>
{
    public OrderListQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A página deve ser maior ou igual a 1.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0)
                .WithMessage("O tamanho da página deve estar entre 1 e 100.")
            .LessThanOrEqualTo(100)
                .WithMessage("O tamanho da página deve estar entre 1 e 100.");

        // from/to são inclusivos; from depois de to produziria um conjunto vazio por engano.
        RuleFor(x => x.From)
            .Must((query, from) => query.To is not { } to
                                   || from is not { } inicial
                                   || inicial <= to)
            .WithMessage("A data inicial não pode ser maior que a data final.");

        RuleFor(x => x.Kind)
            .Must(kind => kind is null || Enum.IsDefined(typeof(OrderKind), kind.Value))
            .WithMessage("O tipo do pedido é inválido.");

        RuleFor(x => x.Channel)
            .MaximumLength(60)
            .WithMessage("O canal deve ter no máximo 60 caracteres.");

        RuleFor(x => x.Search)
            .MaximumLength(60)
            .WithMessage("A busca deve ter no máximo 60 caracteres.");
    }
}
