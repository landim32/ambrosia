namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Relógio injetável do domínio. Exists para que validators ("data não futura") e o resumo do
/// cliente ("dias desde a última compra") tenham um "hoje" controlável nos testes, sem estado
/// estático compartilhado entre testes paralelos (ajuste de tasks.md sobre o §6 do research).
/// </summary>
public interface IClock
{
    /// <summary>
    /// Data/hora atual em UTC com <see cref="DateTime.Kind"/> = <see cref="DateTimeKind.Unspecified"/>,
    /// exigido pelas colunas <c>timestamp without time zone</c> no Npgsql 6+ (research §6).
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>Data atual (para regras de "não futura" e contagem de dias).</summary>
    DateOnly Today { get; }
}
