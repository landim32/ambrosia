using Ambrosio.DTO.Customers;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Validação de formato de <see cref="CustomerUpdateInfo"/> (FR-009): mesmas regras de nome da
/// inserção.
/// </summary>
public class CustomerUpdateInfoValidator : AbstractValidator<CustomerUpdateInfo>
{
    public CustomerUpdateInfoValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O nome é obrigatório.")
            .Length(2, 200)
                .WithMessage("O nome deve ter entre 2 e 200 caracteres.");
    }
}
