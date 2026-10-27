using Ambrosio.DTO.Auth;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Validação de formato de <see cref="GoogleLoginInfo"/> (T039). A validação criptográfica do
/// ID token pertence ao AppService do provedor (T044), não ao validator (research §8).
/// </summary>
public class GoogleLoginInfoValidator : AbstractValidator<GoogleLoginInfo>
{
    public GoogleLoginInfoValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty()
            .WithMessage("O token do Google é obrigatório.");
    }
}
