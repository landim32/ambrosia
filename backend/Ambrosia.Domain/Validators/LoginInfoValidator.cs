using Ambrosia.DTO.Auth;
using FluentValidation;

namespace Ambrosia.Domain.Validators;

/// <summary>
/// Validação de formato de <see cref="LoginInfo"/> (research §5). Diferente do cadastro, o
/// <b>comprimento da senha não é validado</b>: qualquer senha errada — curta, longa ou do tamanho
/// certo — cai no 401 genérico da FR-008, para que o login não revele nada sobre a conta.
/// </summary>
public class LoginInfoValidator : AbstractValidator<LoginInfo>
{
    public LoginInfoValidator()
    {
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O e-mail é obrigatório.")
            .EmailAddress()
                .WithMessage("E-mail inválido.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("A senha é obrigatória.");
    }
}
