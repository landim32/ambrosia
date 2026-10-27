using Ambrosio.DTO.Customers;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Limites de paginação de <c>GET /customers</c> (FR-008, research §9): página ≥ 1 e
/// pageSize 1-100, para proteger o servidor contra consultas gigantes.
/// </summary>
public class CustomerListQueryValidator : AbstractValidator<CustomerListQuery>
{
    public CustomerListQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A página deve ser maior ou igual a 1.");

        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(1)
                .WithMessage("O tamanho da página deve estar entre 1 e 100.")
            .LessThanOrEqualTo(100)
                .WithMessage("O tamanho da página deve estar entre 1 e 100.");
    }
}
