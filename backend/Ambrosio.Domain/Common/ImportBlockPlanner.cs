namespace Ambrosio.Domain.Common;

/// <summary>
/// Divide o período da importação em blocos consecutivos de N dias (feature 006, FR-009, research §11).
/// <para>
/// Regra: bloco <c>k</c> cobre <c>[início, min(início + blockDays - 1, fim)]</c>, numerado a partir de
/// 1, sem lacuna e sem sobreposição; o último pode ser menor; um período de um dia gera um único
/// bloco. É o desenho que torna "Repetir este bloco" possível (US3) e o progresso observável.
/// </para>
/// <para>Regra pura de domínio: sem relógio, sem banco, sem infraestrutura.</para>
/// </summary>
public static class ImportBlockPlanner
{
    /// <summary>
    /// Blocos do período, em ordem.
    /// </summary>
    /// <param name="start">Primeiro dia, inclusive.</param>
    /// <param name="end">Último dia, inclusive.</param>
    /// <param name="blockDays">Tamanho do bloco em dias (1–31, já validado no startup).</param>
    /// <returns>
    /// Uma lista de <c>(BlockNo, StartDate, EndDate)</c> 1-based cobrindo o período inteiro. Vazia
    /// quando o período é inválido (fim antes do início) — quem chama valida antes (FR-003).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockDays"/> menor que 1.</exception>
    public static List<(int BlockNo, DateOnly StartDate, DateOnly EndDate)> Split(
        DateOnly start,
        DateOnly end,
        int blockDays)
    {
        if (blockDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(blockDays), blockDays, "O bloco precisa ter ao menos um dia.");
        }

        var blocks = new List<(int BlockNo, DateOnly StartDate, DateOnly EndDate)>();

        if (end < start)
        {
            return blocks;
        }

        var cursor = start;

        while (cursor <= end)
        {
            // blockDays dias a partir de cursor, inclusive: cursor + (blockDays - 1), cortado no fim.
            var candidate = cursor.AddDays(blockDays - 1);
            var blockEnd = candidate > end ? end : candidate;

            blocks.Add((blocks.Count + 1, cursor, blockEnd));

            cursor = blockEnd.AddDays(1);
        }

        return blocks;
    }

    /// <summary>Quantos blocos o período produz (mesma regra de <see cref="Split"/>, sem alocar a lista).</summary>
    public static int CountBlocks(DateOnly start, DateOnly end, int blockDays)
    {
        if (blockDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(blockDays), blockDays, "O bloco precisa ter ao menos um dia.");
        }

        if (end < start)
        {
            return 0;
        }

        var days = end.DayNumber - start.DayNumber + 1;

        return (days + blockDays - 1) / blockDays;
    }
}
