using Ambrosio.DTO.Imports;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Paginação do histórico de importações (feature 006, contrato imports-api.md): <c>page</c> ≥ 1 e
/// <c>pageSize</c> de 1 a 50. O teto é menor que o dos pedidos porque cada item do histórico carrega
/// nove contagens e a tela os lista em coluna.
/// </summary>
public class ImportRunListQueryValidator : AbstractValidator<ImportRunListQuery>
{
    private const int MAX_PAGE_SIZE = 50;

    public ImportRunListQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A página deve ser maior ou igual a 1.");

        RuleFor(x => x.PageSize)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0)
                .WithMessage($"O tamanho da página deve estar entre 1 e {MAX_PAGE_SIZE}.")
            .LessThanOrEqualTo(MAX_PAGE_SIZE)
                .WithMessage($"O tamanho da página deve estar entre 1 e {MAX_PAGE_SIZE}.");
    }
}
