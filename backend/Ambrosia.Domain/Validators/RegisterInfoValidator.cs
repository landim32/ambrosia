using Ambrosia.DTO.Auth;
using FluentValidation;

namespace Ambrosia.Domain.Validators;

/// <summary>
/// Validação de formato de <see cref="RegisterInfo"/> (FR-003, research §5), com mensagens em
/// português citando o campo (constituição §IV). Os limites são os da spec (nome 120, e-mail
/// 254) e são mais estritos que as colunas <c>name varchar(200)</c> / <c>email varchar(260)</c>.
/// </summary>
public class RegisterInfoValidator : AbstractValidator<RegisterInfo>
{
    /// <summary>Comprimento mínimo da senha — o custo do PBKDF2 é o que a protege, não a composição.</summary>
    private const int PASSWORD_MIN_LENGTH = 8;

    /// <summary>Limite superior para caber no hash e não estourar a meta de 500 ms p95.</summary>
    private const int PASSWORD_MAX_LENGTH = 128;

    public RegisterInfoValidator()
    {
        // CascadeMode.Stop: sem ele um e-mail vazio acumularia "obrigatório" + "inválido", e o
        // formulário mostraria o erro que não descreve o problema (mesmo motivo de
        // GoogleLoginInfoValidator).
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O nome é obrigatório.")
            // O corte acontece antes da medição: " Ana " com 121 caracteres útil é recusado,
            // mas 120 caracteres mais espaços não (FR-003).
            .Must(name => name.Trim().Length <= 120)
                .WithMessage("O nome deve ter no máximo 120 caracteres.");

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O e-mail é obrigatório.")
            .EmailAddress()
                .WithMessage("E-mail inválido.")
            .MaximumLength(254)
                .WithMessage("O e-mail deve ter no máximo 254 caracteres.");

        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("A senha é obrigatória.")
            // Sem Trim() aqui: espaços nas pontas fazem parte da senha (edge case da spec) —
            // o mesmo valor tem de valer no login.
            .MinimumLength(PASSWORD_MIN_LENGTH)
                .WithMessage($"A senha deve ter entre {PASSWORD_MIN_LENGTH} e {PASSWORD_MAX_LENGTH} caracteres.")
            .MaximumLength(PASSWORD_MAX_LENGTH)
                .WithMessage($"A senha deve ter entre {PASSWORD_MIN_LENGTH} e {PASSWORD_MAX_LENGTH} caracteres.");
    }
}
