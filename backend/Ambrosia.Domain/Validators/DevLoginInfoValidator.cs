using Ambrosia.DTO.Auth;
using FluentValidation;

namespace Ambrosia.Domain.Validators;

/// <summary>
/// Validação de formato de <see cref="DevLoginInfo"/> (T039), com mensagens em português citando
/// o campo (FR-024). Os limites vêm das colunas de <c>users</c>: <c>email varchar(260)</c> e
/// <c>name varchar(200)</c> (data-model).
/// </summary>
public class DevLoginInfoValidator : AbstractValidator<DevLoginInfo>
{
    public DevLoginInfoValidator()
    {
        // CascadeMode.Stop: sem ele um e-mail vazio acumularia "obrigatório" + "inválido", e a
        // FR-024 pede o erro do campo na mensagem que realmente descreve o problema.
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O e-mail é obrigatório.")
            .EmailAddress()
                .WithMessage("E-mail inválido.")
            .MaximumLength(260)
                .WithMessage("O e-mail deve ter no máximo 260 caracteres.");

        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O nome é obrigatório.")
            .MaximumLength(200)
                .WithMessage("O nome deve ter no máximo 200 caracteres.");
    }
}
