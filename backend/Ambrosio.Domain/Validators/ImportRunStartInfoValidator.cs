using Ambrosio.Domain.Common;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Settings;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Validação do início de uma importação (feature 006 FR-003/FR-004, reescrita pela feature 008:
/// research R6 e data-model §5). Só o período — as credenciais saíram daqui quando deixaram de vir no
/// corpo (FR-001/FR-002 da 008); a checagem de "há credenciais cadastradas" é do
/// <c>ImportRunService.StartAsync</c>, porque depende do banco.
/// <para>
/// As mesmas regras valem para o <c>POST /runs/update-base</c> e para o plugin (FR-015 da 008), e é por
/// isso que a mensagem do limite cita o número configurado: tela, servidor e plugin têm de repetir a
/// mesma frase (SC-005).
/// </para>
/// <para>
/// <b>Nenhuma mensagem ecoa o valor enviado</b> (FR-006 da 006, mantido): nem a data. Nenhuma regra usa
/// <c>{PropertyValue}</c> — o padrão do FluentValidation interpolaria o valor na mensagem, e a mensagem
/// vai para o corpo da resposta <c>400</c>.
/// </para>
/// </summary>
public class ImportRunStartInfoValidator : AbstractValidator<ImportRunStartInfo>
{
    public ImportRunStartInfoValidator(IClock clock, IOptions<MaisVendasSettings> settings)
    {
        var maxRangeMonths = settings.Value.MaxRangeMonths;

        // "hoje" é o de São Paulo, não o UTC: uma vendedora que importa às 20h está importando hoje
        // (research §11).
        var today = SaoPauloCalendar.Today(clock);

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

        // Limite do período em meses corridos (feature 008, research R6). A conta é por mês porque
        // "3 meses" em dias variaria entre 89 e 92 e a vendedora pensa em meses: 26/07 a 25/10 entra,
        // 26/07 a 26/10 não. O número na mensagem vem da configuração, como antes.
        RuleFor(x => x)
            .Must(info => info.EndDate <= info.StartDate.AddMonths(maxRangeMonths).AddDays(-1))
            .WithMessage($"O período não pode passar de {maxRangeMonths} meses.")
            .OverridePropertyName(nameof(ImportRunStartInfo.EndDate))
            .When(info => info.StartDate != default && info.EndDate != default && info.EndDate >= info.StartDate);
    }
}
