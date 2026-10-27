namespace Ambrosio.Domain.Exceptions;

/// <summary>
/// Novos inícios de importação bloqueados por recusas seguidas de credencial (feature 006, FR-008a).
/// O controller a converte em HTTP <c>429</c> com o cabeçalho <c>Retry-After</c> e a mensagem com o
/// horário de liberação.
/// <para>
/// Carrega só o instante do bloqueio: nem o login, nem a senha, nem quantas tentativas falharam — nada
/// que ajude a sondar a conta de outra pessoa.
/// </para>
/// </summary>
public class ImportLockedException : Exception
{
    /// <summary>Até quando novos inícios estão bloqueados (UTC, coluna <c>locked_until</c>).</summary>
    public DateTime LockedUntil { get; }

    public ImportLockedException(DateTime lockedUntil)
        : base("Muitas tentativas com credenciais recusadas.") =>
        LockedUntil = lockedUntil;

    public ImportLockedException(DateTime lockedUntil, string message)
        : base(message) =>
        LockedUntil = lockedUntil;
}
