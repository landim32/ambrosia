using Ambrosio.Domain.Interfaces;

namespace Ambrosio.Domain.Common;

/// <summary>
/// O "hoje" da vendedora (feature 006, research §11). O +Vendas é um sistema brasileiro e a tela
/// trabalha em datas locais: comparar <c>endDate</c> com <c>DateTime.UtcNow</c> recusaria como
/// "futuro" a data de hoje de uma vendedora às 20h (UTC já seria amanhã), ou aceitaria ontem errado às
/// 21h. Todo limite de período e a sugestão de data final passam por aqui.
/// <para>
/// Usa <see cref="IClock"/> (injetável, para os testes terem um "hoje" controlável) e o fuso por ID do
/// sistema: <c>America/Sao_Paulo</c> resolve no Linux com <c>tzdata</c> (a imagem aspnet o traz) e no
/// Windows via ICU.
/// </para>
/// </summary>
public static class SaoPauloCalendar
{
    /// <summary>Identificador do fuso de São Paulo nos dois sistemas de runtime suportados.</summary>
    public const string TIME_ZONE_ID = "America/Sao_Paulo";

    private static readonly TimeZoneInfo SaoPaulo = Resolve();

    /// <summary>Hoje em São Paulo, a partir do instante UTC do relógio injetado.</summary>
    public static DateOnly Today(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc), SaoPaulo));
    }

    /// <summary>O mesmo instante UTC traduzido para o relógio de São Paulo (para as mensagens de bloqueio).</summary>
    public static DateTime ToSaoPaulo(DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), SaoPaulo);

    /// <summary>Horário de liberação formatado (<c>HH:mm</c>) no fuso local, para a FR-008a.</summary>
    public static string FormatTime(DateTime utcNow) => ToSaoPaulo(utcNow).ToString("HH:mm");

    /// <summary>
    /// O dia de <paramref name="date"/> há <paramref name="months"/> meses (feature 008, research R6).
    /// <para>
    /// "Três meses atrás" não tem um número fixo de dias (89, 90 ou 92), e a vendedora pensa em meses:
    /// o limite de período e o corte do "Atualizar base" usam este cálculo, que casa com a regra do
    /// validator (<c>endDate &lt;= startDate.AddMonths(n).AddDays(-1)</c>).
    /// </para>
    /// <para>
    /// Quando o mês de destino não tem aquele dia (31 voltando para fevereiro), o resultado é o
    /// <b>último dia</b> do mês de destino — nunca o dia 1 do mês seguinte, que encurtaria o período
    /// configurado. Testado com dias 29, 30 e 31 em mês de 28, 29 e 30 dias.
    /// </para>
    /// </summary>
    public static DateOnly AddMonthsFloor(DateOnly date, int months)
    {
        var target = new DateOnly(date.Year, date.Month, 1).AddMonths(-months);
        var lastDayOfMonth = System.DateTime.DaysInMonth(target.Year, target.Month);

        return new DateOnly(target.Year, target.Month, Math.Min(date.Day, lastDayOfMonth));
    }

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TIME_ZONE_ID);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback de ambiente mínimo (container sem tzdata, por exemplo): o deslocamento fixo de
            // Brasília, sem horário de verão. Melhor uma hora aproximada do que o startup quebrar.
            return TimeZoneInfo.CreateCustomTimeZone(
                TIME_ZONE_ID,
                TimeSpan.FromHours(-3),
                "Horário de Brasília (fallback)",
                "Horário de Brasília (fallback)");
        }
    }
}
