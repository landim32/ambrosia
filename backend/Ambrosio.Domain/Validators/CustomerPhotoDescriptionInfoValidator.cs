using Ambrosio.DTO.CustomerPhotos;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Formato de <c>PUT /customers/{customerId}/photos/{photoId}/description</c> (feature 009, FR-007).
/// <c>null</c> é valor válido — é assim que a vendedora apaga a legenda (cenário US4-4).
/// </summary>
public class CustomerPhotoDescriptionInfoValidator : AbstractValidator<CustomerPhotoDescriptionInfo>
{
    public CustomerPhotoDescriptionInfoValidator()
    {
        RuleFor(x => x.Description)
            .MaximumLength(500)
                .WithMessage("A descrição deve ter no máximo 500 caracteres.");
    }
}
