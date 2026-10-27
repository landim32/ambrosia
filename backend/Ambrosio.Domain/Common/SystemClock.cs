using Ambrosio.Domain.Interfaces;

namespace Ambrosio.Domain.Common;

/// <summary>
/// Implementação padrão de <see cref="IClock"/> baseada no relógio do sistema.
/// Registrada como singleton em <c>Ambrosio.Application/Startup.cs</c> (T022).
/// </summary>
public class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
