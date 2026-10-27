using Ambrosio.DTO.Images;
using Ambrosio.DTO.Settings;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Validação de formato da declaração de envio (FR-003, FR-005). Os limites vêm da configuração, não
/// de constante: <c>AllowedMimeTypes</c> e <c>MaxUploadBytes</c> são o que o ambiente aceita, então
/// recusar um tipo não suportado é decisão de ambiente (research §12).
/// <para>
/// Aqui só se valida o <b>declarado</b>. A conferência contra o que realmente chegou ao storage —
/// tamanho, tipo pelo conteúdo e hash — é a confirmação (FR-006, FR-007) e exige o storage, portanto
/// fica no serviço (skill <c>dotnet-fluent-validation</c>: regra de negócio com recurso externo não vai
/// para o validator).
/// </para>
/// </summary>
public class ImageUploadInsertInfoValidator : AbstractValidator<ImageUploadInsertInfo>
{
    public ImageUploadInsertInfoValidator(IOptions<ObjectStorageSettings> settings)
    {
        var objectStorage = settings.Value;
        var maxUploadBytes = objectStorage.MaxUploadBytes;

        RuleFor(x => x.MimeType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O campo mimeType é obrigatório.")
            .Must(mimeType => IsAllowed(mimeType, objectStorage))
                .WithMessage($"O campo mimeType deve ser um tipo aceito: {DescribeAllowed(objectStorage)}.");

        RuleFor(x => x.SizeBytes)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0)
                .WithMessage("O campo sizeBytes deve ser maior que zero.")
            .LessThanOrEqualTo(maxUploadBytes)
                .WithMessage($"O campo sizeBytes não pode passar de {maxUploadBytes} bytes.");

        RuleFor(x => x.Sha256)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("O campo sha256 é obrigatório.")
            .Matches("^[0-9a-fA-F]{64}$")
                .WithMessage("O campo sha256 deve ter exatamente 64 caracteres hexadecimais.");
    }

    private static bool IsAllowed(string? mimeType, ObjectStorageSettings settings)
    {
        return !string.IsNullOrWhiteSpace(mimeType)
               && settings.AllowedMimeTypes.Any(allowed => string.Equals(allowed, mimeType.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A lista aceita citada na mensagem, para o cliente entender o erro sem consultar doc.</summary>
    private static string DescribeAllowed(ObjectStorageSettings settings) =>
        string.Join(", ", settings.AllowedMimeTypes);
}
