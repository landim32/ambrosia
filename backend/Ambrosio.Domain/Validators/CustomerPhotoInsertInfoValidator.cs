using Ambrosio.DTO.CustomerPhotos;
using Ambrosio.DTO.Enums;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Formato de <c>POST /customers/{customerId}/photos</c> (feature 009, data-model §7).
/// A existência, o dono e a situação <c>available</c> da imagem exigem banco e por isso ficam no
/// serviço (skill <c>dotnet-fluent-validation</c>: regra de negócio não mora no validator).
/// </summary>
public class CustomerPhotoInsertInfoValidator : AbstractValidator<CustomerPhotoInsertInfo>
{
    public CustomerPhotoInsertInfoValidator()
    {
        RuleFor(x => x.ImageId)
            .GreaterThan(0)
                .WithMessage("A imagem da foto é obrigatória.");

        RuleFor(x => x.Description)
            .MaximumLength(500)
                .WithMessage("A descrição deve ter no máximo 500 caracteres.");

        // `chatGpt` é recusado aqui de propósito: a conversa tem o canal próprio (a tool), e aceitar
        // o valor por esta rota permitiria que uma integração se passasse pelo ChatGPT (research R12).
        RuleFor(x => x.Source)
            .Must(source => source is null
                            || source == CustomerPhotoSource.Web
                            || source == CustomerPhotoSource.Api)
                .WithMessage("A origem informada não é aceita nesta rota.");
    }
}
