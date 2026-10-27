using System.Globalization;
using System.Text.RegularExpressions;
using Ambrosia.Domain.ValueObjects;

namespace Ambrosia.Domain.Common;

/// <summary>
/// Conversão do texto do +Vendas para os tipos do Domain (feature 006, FR-029/FR-031, research §10).
/// <para>
/// Regras puras de domínio, sem IO: a origem entrega dinheiro como <c>R$ 1.234,56</c> (e negativo como
/// <c>-R$ 1.234,56</c> ou <c>(R$ 1.234,56)</c>), datas de lista como <c>24 Out</c> — sem ano — e CPF
/// com pontuação variável. O ano é <b>deduzido do intervalo do bloco</b> e <b>confirmado</b> pelo
/// segmento <c>AAAA-MM-DD</c> da URL de detalhe: as duas fontes precisam concordar, porque um erro de
/// ano criaria um pedido com chave natural errada e duplicaria a venda.
/// </para>
/// </summary>
public static partial class MaisVendasNormalizer
{
    /// <summary>Nome do mês em português (a origem exibe <c>Jan, Fev, Mar, Abr, Mai, Jun, Jul, Ago, Set, Out, Nov, Dez</c>).</summary>
    private static readonly Dictionary<string, int> MonthByAbbreviation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["janeiro"] = 1,
        ["fev"] = 2, ["fevereiro"] = 2,
        ["mar"] = 3, ["marco"] = 3, ["már"] = 3, ["março"] = 3,
        ["abr"] = 4, ["abril"] = 4,
        ["mai"] = 5, ["maio"] = 5,
        ["jun"] = 6, ["junho"] = 6,
        ["jul"] = 7, ["julho"] = 7,
        ["ago"] = 8, ["agosto"] = 8,
        ["set"] = 9, ["setembro"] = 9,
        ["out"] = 10, ["outubro"] = 10,
        ["nov"] = 11, ["novembro"] = 11,
        ["dez"] = 12, ["dezembro"] = 12
    };

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Abreviatura de três letras de cada mês (1-based), exatamente como a lista da origem exibe
    /// (<c>24 Out</c>). Serve também ao coletor Fake para produzir o cenário <c>sem-ano</c>.
    /// </summary>
    public static string MonthAbbreviation(int month) => month switch
    {
        1 => "Jan",
        2 => "Fev",
        3 => "Mar",
        4 => "Abr",
        5 => "Mai",
        6 => "Jun",
        7 => "Jul",
        8 => "Ago",
        9 => "Set",
        10 => "Out",
        11 => "Nov",
        12 => "Dez",
        _ => throw new ArgumentOutOfRangeException(nameof(month), month, "Mês fora de 1 a 12.")
    };

    /// <summary>
    /// <c>R$ 1.234,56</c> → <c>1234.56m</c>. Aceita sinal antes ou depois do valor, parênteses de
    /// negativo, ponto de milhar opcional e casa decimal de 1 ou 2 dígitos.
    /// </summary>
    /// <returns><c>false</c> quando o texto não é um valor monetário reconhecível.</returns>
    public static bool TryParseMoney(string? value, out decimal total)
    {
        total = 0m;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var negative = false;

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            negative = true;
            text = text[1..^1].Trim();
        }

        text = text.Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

        if (text.StartsWith('-'))
        {
            negative = true;
            text = text[1..].Trim();
        }
        else if (text.StartsWith('+'))
        {
            text = text[1..].Trim();
        }

        if (text.EndsWith('-'))
        {
            negative = true;
            text = text[..^1].Trim();
        }

        // Só dígitos, ponto (milhar) e vírgula (decimal) sobram depois de tirar espaços.
        var cleaned = text.Replace(" ", string.Empty).Replace(" ", string.Empty);

        if (cleaned.Length == 0 || !MoneyDigitsRegex().IsMatch(cleaned))
        {
            return false;
        }

        // Última vírgula é o separador decimal; os demais pontos são milhar.
        var decimalIndex = cleaned.LastIndexOf(',');

        if (decimalIndex >= 0)
        {
            var integerPart = cleaned[..decimalIndex].Replace(".", string.Empty);
            var fractionPart = cleaned[(decimalIndex + 1)..];

            cleaned = fractionPart.Length == 1
                ? $"{integerPart}.{fractionPart}0"
                : $"{integerPart}.{fractionPart}";
        }
        else
        {
            cleaned = cleaned.Replace(".", string.Empty);
        }

        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        total = Math.Round(negative ? -parsed : parsed, 2, MidpointRounding.AwayFromZero);

        return true;
    }

    /// <summary>
    /// Converte o total e aplica o sinal do tipo: devolução e cancelamento entram negativos na base,
    /// como no +Vendas (FR-013 da feature 001, FR-029 daqui). Valor já negativo com tipo "venda" é
    /// mantido como veio — é a origem quem manda.
    /// </summary>
    public static decimal NormalizeMoney(DTO.Enums.OrderKind kind, string? value)
    {
        if (!TryParseMoney(value, out var parsed))
        {
            throw new FormatException("A origem trouxe um valor monetário em formato não reconhecido.");
        }

        return kind == DTO.Enums.OrderKind.Sale ? parsed : -Math.Abs(parsed);
    }

    /// <summary>
    /// Só dígitos do CPF, validado pelo value object existente. <c>null</c> quando ausente, com
    /// tamanho errado, dígito verificador inválido ou sequência repetida — quem chama decide que é
    /// pedido "com erro" (FR-036), sem nunca ecoar o documento.
    /// </summary>
    public static string? NormalizeCpf(string? value) =>
        Cpf.TryParse(value, out var cpf) ? cpf!.Digits : null;

    /// <summary>Máscara do CPF para log e rastro (FR-052); <c>null</c> quando não há CPF válido.</summary>
    public static string? MaskCpf(string? cpfDigits) =>
        Cpf.TryParse(cpfDigits, out var cpf) ? cpf!.Masked : null;

    /// <summary>
    /// Data de lista do +Vendas (<c>24 Out</c>, <c>24/10</c>, <c>24/10/2026</c>) com o ano resolvido
    /// pelo intervalo do bloco.
    /// <para>
    /// A dedução escolhe o ano candidato (dentro do intervalo, ou o mais próximo que faz sentido quando
    /// o dia/mês não cai no intervalo) e <b>confirma</b> contra o ano da URL de detalhe quando ele é
    /// informado. Divergência lança sem nenhum dado pessoal na mensagem (FR-031) — o bloco falha, e a
    /// vendedora repete depois, em vez de um pedido entrar com a chave natural errada.
    /// </para>
    /// </summary>
    /// <param name="value">Texto da data como a origem exibe.</param>
    /// <param name="blockStart">Primeiro dia do bloco.</param>
    /// <param name="blockEnd">Último dia do bloco.</param>
    /// <param name="yearFromDetailUrl">Ano lido do segmento <c>AAAA-MM-DD</c> da URL de detalhe, quando houver.</param>
    /// <exception cref="FormatException">Data ausente, em formato não reconhecido ou com ano divergente.</exception>
    public static DateOnly ResolveDate(
        string? value,
        DateOnly blockStart,
        DateOnly blockEnd,
        int? yearFromDetailUrl = null)
    {
        if (!TryReadDayMonth(value, out var day, out var month, out var explicitYear))
        {
            throw new FormatException(
                $"A origem trouxe uma data em formato não reconhecido (dia/mês lidos de \"{Truncate(value)}\").");
        }

        // Ano explícito no próprio texto: é a melhor fonte que existe, e só precisa bater com a URL.
        if (explicitYear is { } year)
        {
            EnsureUrlAgrees(year, yearFromDetailUrl, value);

            return BuildDate(year, month, day, value);
        }

        if (yearFromDetailUrl is { } urlYear)
        {
            return BuildDate(urlYear, month, day, value);
        }

        // Candidatos: os anos tocados pelo intervalo, mais um de cada lado, para o caso de virada de
        // ano em que a data da lista cai fora do intervalo por erro de leitura. A ordem é a de
        // proximidade do meio do bloco, e só se aceita a que cair dentro dele.
        var middle = blockStart.AddDays((blockEnd.DayNumber - blockStart.DayNumber) / 2);

        var candidates = new[] { blockStart.Year, blockEnd.Year, blockStart.Year - 1, blockEnd.Year + 1 }
            .Distinct()
            .Where(y => BuildSafeDate(y, month, day) is not null)
            .OrderBy(y => Math.Abs(BuildSafeDate(y, month, day)!.Value.DayNumber - middle.DayNumber))
            .ToList();

        foreach (var candidateYear in candidates)
        {
            var candidate = BuildSafeDate(candidateYear, month, day)!.Value;

            // Dentro do intervalo do bloco é a dedução direta (inclusive virada de ano: bloco
            // 28/12–03/01 com "02 Jan" resolve para o ano de blockEnd).
            if (candidate >= blockStart && candidate <= blockEnd)
            {
                return candidate;
            }
        }

        // Fora do intervalo: o dia/mês não pertence a este bloco. É divergência da origem, e o bloco
        // falha antes de gravar (FR-010).
        throw new FormatException(
            $"A data \"{Truncate(value)}\" do pedido não cai no intervalo do bloco " +
            $"{blockStart:dd/MM/yyyy}–{blockEnd:dd/MM/yyyy}.");
    }

    /// <summary>
    /// Extrai o ano do segmento <c>AAAA-MM-DD</c> da URL de detalhe
    /// (ex.: <c>/detailOrder/.../2026-10-05/...</c>). <c>null</c> quando a URL não traz o segmento.
    /// </summary>
    public static int? TryReadYearFromDetailUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = IsoDateInPathRegex().Match(url);

        return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : null;
    }

    /// <summary>Lê <c>dd</c>, <c>MM</c> e o ano quando o texto o traz. Aceita <c>24 Out</c>, <c>24/10</c>, <c>24/10/2026</c>, <c>2026-10-24</c>.</summary>
    private static bool TryReadDayMonth(string? value, out int day, out int month, out int? year)
    {
        day = 0;
        month = 0;
        year = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        // ISO primeiro: 2026-10-24.
        var iso = IsoDateRegex().Match(text);

        if (iso.Success)
        {
            year = int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture);
            month = int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture);
            day = int.Parse(iso.Groups[3].Value, CultureInfo.InvariantCulture);

            return true;
        }

        // 24/10 ou 24/10/2026 (também com ponto).
        var numeric = NumericDateRegex().Match(text);

        if (numeric.Success)
        {
            day = int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture);
            month = int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture);

            if (numeric.Groups[3].Success)
            {
                var candidate = int.Parse(numeric.Groups[3].Value, CultureInfo.InvariantCulture);

                // Dois dígitos = 20YY; a origem nunca usa século outro.
                year = numeric.Groups[3].Value.Length == 2 ? 2000 + candidate : candidate;
            }

            return true;
        }

        // "24 Out" / "24 Out 2026" / "24 de Outubro".
        var parts = MonthTextRegex().Match(text);

        if (parts.Success && MonthByAbbreviation.TryGetValue(parts.Groups[2].Value, out var namedMonth))
        {
            day = int.Parse(parts.Groups[1].Value, CultureInfo.InvariantCulture);
            month = namedMonth;

            if (parts.Groups[3].Success)
            {
                year = int.Parse(parts.Groups[3].Value.Trim(), CultureInfo.InvariantCulture);
            }

            return true;
        }

        // Fallback: o formato do pt-BR com separador de data do idioma.
        if (DateTime.TryParse(text, PtBr, DateTimeStyles.None, out var parsed))
        {
            day = parsed.Day;
            month = parsed.Month;
            year = parsed.Year;

            return true;
        }

        return false;
    }

    private static void EnsureUrlAgrees(int year, int? yearFromDetailUrl, string? value)
    {
        if (yearFromDetailUrl is { } urlYear && urlYear != year)
        {
            throw new FormatException(
                $"A data do pedido (\"{Truncate(value)}\", ano {year}) diverge da URL de detalhe (ano {urlYear}).");
        }
    }

    private static DateOnly BuildDate(int year, int month, int day, string? value)
    {
        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new FormatException($"A origem trouxe uma data inválida (\"{Truncate(value)}\").");
        }
    }

    private static DateOnly? BuildSafeDate(int year, int month, int day)
    {
        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        // O texto da data é dado de origem público (não é CPF nem nome), mas continua curto: a mensagem
        // vai para import_run_blocks.error_message, que tem 500 caracteres.
        return trimmed.Length <= 40 ? trimmed : trimmed[..40];
    }

    /// <summary>Sobra de um valor monetário: dígitos com <c>.</c> de milhar e <c>,</c> decimal.</summary>
    [GeneratedRegex(@"^\d{1,3}(?:\.\d{3})*(?:,\d{1,2})?$|^\d+(?:,\d{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyDigitsRegex();

    /// <summary><c>2026-10-24</c>.</summary>
    [GeneratedRegex(@"^(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();

    /// <summary>O segmento <c>AAAA-MM-DD</c> em qualquer posição da URL de detalhe.</summary>
    [GeneratedRegex(@"/(\d{4})-(\d{2})-(\d{2})(?=[/?#]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateInPathRegex();

    /// <summary><c>24/10</c>, <c>24/10/2026</c> ou <c>24.10.2026</c>.</summary>
    [GeneratedRegex(@"^(\d{1,2})[/.\-](\d{1,2})(?:[/.\-](\d{2,4}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDateRegex();

    /// <summary><c>24 Out</c>, <c>24 de Outubro</c>, <c>24 Out 2026</c>.</summary>
    [GeneratedRegex(@"^(\d{1,2})\s*(?:de\s*)?([A-Za-zÀ-ÿ]+)(?:\s+(\d{4}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthTextRegex();
}
