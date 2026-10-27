using Ambrosia.Domain.Common;
using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Imports;
using Ambrosia.DTO.Settings;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Ambrosia.Domain.Validators;

/// <summary>
/// Validação do início de uma importação (feature 006, FR-003/FR-004/FR-006, data-model §5,
/// research §11). As regras que a tela também aplica — o servidor recusa a mesma requisição enviada
/// por Swagger ou Bruno sem passar pela tela (spec US1-5).
/// <para>
/// <b>Nenhuma mensagem ecoa o valor enviado</b> (FR-006): nem o login, nem a senha, nem a data. Por
/// isso nenhuma regra usa <c>{PropertyValue}</c> — o padrão do FluentValidation interpolaria o valor
/// na mensagem, e a mensagem vai para o corpo da resposta <c>400</c>.
/// </para>
/// </summary>
public class ImportRunStartInfoValidator : AbstractValidator<ImportRunStartInfo>
{
    /// <summary>Mesmo teto da coluna de login; acima disso é tentativa de despejar payload.</summary>
    private const int LOGIN_MAX_LENGTH = 120;

    /// <summary>Mesmo teto da coluna de senha; a senha nem é persistida, mas o limite protege o binding.</summary>
    private const int PASSWORD_MAX_LENGTH = 200;

    public ImportRunStartInfoValidator(IClock clock, IOptions<MaisVendasSettings> settings)
    {
        var maxRangeDays = settings.Value.MaxRangeDays;

        // "hoje" é o de São Paulo, não o UTC: uma vendedora que importa às 20h está importando hoje
        // (research §11).
        var today = SaoPauloCalendar.Today(clock);

        RuleFor(x => x.Login)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("Informe o login do +Vendas.")
            .MaximumLength(LOGIN_MAX_LENGTH)
                .WithMessage("Informe o login do +Vendas.");

        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("Informe a senha do +Vendas.")
            .MaximumLength(PASSWORD_MAX_LENGTH)
                .WithMessage("Informe a senha do +Vendas.");

        // DateOnly ausente no JSON chega como 0001-01-01 (default estrutural): é recusado como "não
        // informada", no campo que faltou, e as demais regras do campo não rodam (CascadeMode.Stop).
        RuleFor(x => x.StartDate)
            .Cascade(CascadeMode.Stop)
            .Must(date => date != default)
                .WithMessage("Informe a data inicial do período.")
            .Must(date => date.Year > 2000)
                .WithMessage("Informe a data inicial do período.");

        RuleFor(x => x.EndDate)
            .Cascade(CascadeMode.Stop)
            .Must(date => date != default)
                .WithMessage("Informe a data final do período.")
            .Must(date => date.Year > 2000)
                .WithMessage("Informe a data final do período.")
            .Must((_, date) => date <= today)
                .WithMessage("A data final não pode estar no futuro.");

        // Ordem do período: apontamos startDate, que é o campo que o usuário corrige.
        RuleFor(x => x.StartDate)
            .Must((info, start) => info.EndDate == default || start <= info.EndDate)
            .WithMessage("A data inicial deve ser anterior ou igual à data final.")
            .When(info => info.StartDate != default && info.EndDate != default);

        // Limite do período, com o número da configuração na mensagem (FR-004).
        RuleFor(x => x)
            .Must(info => RangeDays(info) <= maxRangeDays)
            .WithMessage($"O período não pode ultrapassar {maxRangeDays} dias.")
            .OverridePropertyName(nameof(ImportRunStartInfo.EndDate))
            .When(info => info.StartDate != default && info.EndDate != default && info.EndDate >= info.StartDate);
    }

    /// <summary>
    /// Dias inclusivos do período. Com <c>default(DateOnly)</c> no JSON a diferença de
    /// <see cref="DateOnly.DayNumber"/> chega a um bilhão de dias, o que transformaria um simples
    /// "falta a data" numa acusação de período acima do limite — por isso as regras de data rodam com
    /// <c>CascadeMode.Stop</c> e esta conta só é feita quando as duas datas existem (cláusulas
    /// <c>When</c> acima).
    /// </summary>
    private static int RangeDays(ImportRunStartInfo info) => info.EndDate.DayNumber - info.StartDate.DayNumber + 1;
}
