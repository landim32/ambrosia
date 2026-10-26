using Ambrosia.Domain.ValueObjects;
using Ambrosia.DTO.Customers;
using FluentValidation;

namespace Ambrosia.Domain.Validators;

/// <summary>
/// Validação de formato de <see cref="CustomerInsertInfo"/> (FR-006, FR-007). A checagem de
/// duplicidade exige banco e por isso fica no serviço (skill <c>dotnet-fluent-validation</c> /
/// research §8).
/// </summary>
public class CustomerInsertInfoValidator : AbstractValidator<CustomerInsertInfo>
{
    public CustomerInsertInfoValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O nome é obrigatório.")
            .Length(2, 200)
                .WithMessage("O nome deve ter entre 2 e 200 caracteres.");

        RuleFor(x => x.Cpf)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O CPF é obrigatório.")
            .Must(Cpf.IsValid)
                .WithMessage("CPF inválido.");
    }
}
