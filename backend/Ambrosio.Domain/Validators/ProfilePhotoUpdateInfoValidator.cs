using Ambrosio.DTO.CustomerPhotos;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Formato de <c>PUT /customers/{customerId}/profile-photo</c> (feature 009, FR-011/FR-012).
/// Não há validação de enquadramento: o quadrado chega como arquivo já recortado pelo navegador,
/// então as coordenadas nunca atravessam a API (research R4). Pertencimento da foto à cliente e
/// situação da imagem são conferidos no serviço, que conhece o banco.
/// </summary>
public class ProfilePhotoUpdateInfoValidator : AbstractValidator<ProfilePhotoUpdateInfo>
{
    public ProfilePhotoUpdateInfoValidator()
    {
        RuleFor(x => x.PhotoId)
            .GreaterThan(0)
                .WithMessage("A foto de origem da foto de perfil é obrigatória.");

        RuleFor(x => x.ImageId)
            .Must(imageId => imageId is null || imageId > 0)
                .WithMessage("O recorte informado não é uma imagem válida.");
    }
}
