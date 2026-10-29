using Ambrosio.DTO.CustomerPhotos;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Limites de <c>GET /customers/{customerId}/photos</c> (feature 009, contracts/api.md §1).
/// O teto de 60 por página protege a grade: cada item carrega o arquivo original (não há miniatura,
/// research R3), então página grande é tráfego multiplicado no celular da vendedora.
/// </summary>
public class CustomerPhotoListQueryValidator : AbstractValidator<CustomerPhotoListQuery>
{
    public CustomerPhotoListQueryValidator()
    {
        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(1)
                .WithMessage("O tamanho da página deve estar entre 1 e 60.")
            .LessThanOrEqualTo(60)
                .WithMessage("O tamanho da página deve estar entre 1 e 60.");

        // Must em vez de GreaterThan: com long? nulo, a propagação dentro do RuleFor viraria exceção
        // em vez de "sem cursor".
        RuleFor(x => x.Cursor)
            .Must(cursor => cursor is null || cursor > 0)
                .WithMessage("O cursor precisa ser um identificador de foto válido.");
    }
}
