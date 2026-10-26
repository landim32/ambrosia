using Ambrosia.Domain.Interfaces;

namespace Ambrosia.Tests.TestHelpers;

/// <summary>
/// Relógio controlável para testes: congela "agora" e evita estado estático compartilhado
/// entre testes paralelos (motivo da existência de <see cref="IClock"/>).
/// </summary>
public sealed class FakeClock : IClock
{
    public FakeClock() : this(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified))
    {
    }

    public FakeClock(DateTime now)
    {
        Now = now;
    }

    /// <summary>Data/hora congelada (Kind Unspecified, como exige o Npgsql — research §6).</summary>
    public DateTime Now { get; set; }

    public DateTime UtcNow => DateTime.SpecifyKind(Now, DateTimeKind.Unspecified);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
}
