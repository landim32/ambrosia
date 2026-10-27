namespace Ambrosia.Domain.Models;

/// <summary>
/// Vínculo da vendedora com a conta do +Vendas e o bloqueio por recusas (feature 006, tabela
/// <c>mais_vendas_accounts</c>, research §8). Uma linha por vendedora
/// (<c>ux_mais_vendas_accounts_user</c>).
/// <para>
/// <b>Não existe coluna de login nem de senha</b> em lugar nenhum (FR-006): o que se guarda é o
/// HMAC-SHA256 com pepper do identificador da conta, suficiente para perceber que a vendedora tentou
/// importar com outra conta e parar antes de escrever (FR-008b).
/// </para>
/// </summary>
public class MaisVendasAccount
{
    public long Id { get; set; }

    /// <summary>Dono, do claim <c>sub</c>.</summary>
    public long UserId { get; set; }

    /// <summary>Hash com pepper do identificador da conta na origem; <c>null</c> até o primeiro vínculo.</summary>
    public string? AccountHash { get; set; }

    /// <summary>Quando o vínculo foi feito (primeira execução em modo gravação com login aceito).</summary>
    public DateTime? LinkedAt { get; set; }

    /// <summary>Recusas de credencial seguidas; zera em qualquer login aceito (FR-008a).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Até quando novos inícios estão bloqueados; <c>null</c> quando livre.</summary>
    public DateTime? LockedUntil { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }

    public static MaisVendasAccount Create(long userId, DateTime now) => new()
    {
        UserId = userId,
        FailedLoginCount = 0,
        CreatedAt = now,
        UpdatedAt = now
    };

    /// <summary>Há bloqueio em vigor em <paramref name="now"/>?</summary>
    public bool IsLocked(DateTime now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// Registra o vínculo da conta (FR-008b). Só faz sentido em modo <c>apply</c> com login aceito e
    /// sem vínculo anterior — o serviço é quem decide; o modelo só guarda o hash, nunca o identificador.
    /// </summary>
    public void Link(string hash, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("O hash da conta do +Vendas é obrigatório.", nameof(hash));
        }

        AccountHash = hash;
        LinkedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// A conta usada é a vinculada? Sem vínculo, qualquer conta é "a primeira" e o serviço decide se
    /// pode vincular (só em gravação — simulação não vincula, FR-032).
    /// </summary>
    public bool Matches(string hash) =>
        string.Equals(AccountHash, hash, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Uma recusa de credencial (FR-008a): incrementa e, na <paramref name="maxFailedLogins"/>-ésima
    /// recusa <b>seguida</b>, abre o bloqueio de <paramref name="lockoutMinutes"/>. A contagem continua
    /// subindo depois disso (o bloqueio é renovado a cada tentativa recusada), porque o que importa é
    /// o horário de liberação.
    /// </summary>
    /// <returns><c>true</c> quando esta recusa abriu um bloqueio.</returns>
    public bool RegisterRefusedLogin(DateTime now, int maxFailedLogins, int lockoutMinutes)
    {
        FailedLoginCount++;
        UpdatedAt = now;

        if (FailedLoginCount >= maxFailedLogins)
        {
            LockedUntil = now.AddMinutes(lockoutMinutes);

            return true;
        }

        return false;
    }

    /// <summary>Login aceito: a sequência de recusas acaba aqui (FR-008a).</summary>
    public void RegisterAcceptedLogin(DateTime now)
    {
        FailedLoginCount = 0;
        LockedUntil = null;
        UpdatedAt = now;
    }
}
