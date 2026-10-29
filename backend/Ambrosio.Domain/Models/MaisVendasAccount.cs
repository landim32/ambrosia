using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Models;

/// <summary>
/// Vínculo da vendedora com a conta do +Vendas, o bloqueio por recusas (feature 006, tabela
/// <c>mais_vendas_accounts</c>, research §8) e as <b>credenciais cifradas</b> da feature 008
/// (research R1/R7, data-model §1). Uma linha por vendedora (<c>ux_mais_vendas_accounts_user</c>).
/// <para>
/// <b>Credenciais (feature 008)</b>: <see cref="LoginEncrypted"/> e <see cref="PasswordEncrypted"/>
/// guardam o texto cifrado (<c>v1:</c> + AES-256-GCM) — nunca o valor em claro, que é criado e lido
/// pela cifra da Infra com a chave dedicada. As duas colunas são nulas juntas ou preenchidas juntas:
/// <see cref="SetCredentials"/> e <see cref="ClearCredentials"/> são os únicos caminhos que as tocam,
/// e é por isso que a tela consegue afirmar "há senha" sem ler a senha (<c>hasPassword</c>).
/// </para>
/// <para>
/// O que continua de 006: o HMAC-SHA256 com pepper do identificador da conta (<see cref="AccountHash"/>)
/// percebe que a vendedora tentou importar com outra conta (FR-008b), e o bloqueio por recusas
/// (<see cref="LockedUntil"/>) não mudou — a cifra só acrescentou o rastro de que a senha guardada
/// precisa ser atualizada (<see cref="CredentialsStatus"/>, FR-007).
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

    /// <summary>Login do +Vendas cifrado (<c>v1:</c> + AES-256-GCM); <c>null</c> sem credenciais.</summary>
    public string? LoginEncrypted { get; set; }

    /// <summary>Senha do +Vendas cifrada; <c>null</c> sem credenciais. NUNCA sai daqui em claro.</summary>
    public string? PasswordEncrypted { get; set; }

    /// <summary>Última gravação de login ou senha (UTC).</summary>
    public DateTime? CredentialsSavedAt { get; set; }

    /// <summary>Última troca de senha (UTC) — é a data que a tela mostra ("Senha cadastrada em ...").</summary>
    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>Situação das credenciais para a tela e para o plugin (FR-004/FR-007).</summary>
    public MaisVendasCredentialsStatus CredentialsStatus { get; set; } = MaisVendasCredentialsStatus.None;

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
    /// <para>
    /// Com credenciais guardadas (feature 008, FR-007), a recusa também marca
    /// <see cref="CredentialsStatus"/> como <see cref="MaisVendasCredentialsStatus.NeedsUpdate"/>: é
    /// o que faz a tela e o plugin orientarem a vendedora a conferir a senha, sem expor nada. Sem
    /// credenciais (importação com credencial digitada na hora, como até a 006) o status fica
    /// <see cref="MaisVendasCredentialsStatus.None"/>.
    /// </para>
    /// <returns><c>true</c> quando esta recusa abriu um bloqueio.</returns>
    public bool RegisterRefusedLogin(DateTime now, int maxFailedLogins, int lockoutMinutes)
    {
        FailedLoginCount++;
        UpdatedAt = now;

        if (HasCredentials)
        {
            CredentialsStatus = MaisVendasCredentialsStatus.NeedsUpdate;
        }

        if (FailedLoginCount >= maxFailedLogins)
        {
            LockedUntil = now.AddMinutes(lockoutMinutes);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Login aceito: a sequência de recusas acaba aqui (FR-008a) e as credenciais guardadas voltam a
    /// <see cref="MaisVendasCredentialsStatus.Valid"/> (data-model §1 — o +Vendas acaba de provar que
    /// servem).
    /// </summary>
    public void RegisterAcceptedLogin(DateTime now)
    {
        FailedLoginCount = 0;
        LockedUntil = null;
        UpdatedAt = now;

        if (HasCredentials)
        {
            CredentialsStatus = MaisVendasCredentialsStatus.Valid;
        }
    }

    // ------------------------------------------------------------------ credenciais (feature 008)

    /// <summary>
    /// Há as duas credenciais cifradas? É o que o <c>GET</c> devolve como <c>hasPassword</c> sem ler a
    /// senha, e o que o início da importação exige (FR-004/FR-009).
    /// </summary>
    public bool HasCredentials =>
        LoginEncrypted is { Length: > 0 } && PasswordEncrypted is { Length: > 0 };

    /// <summary>
    /// Guarda login e senha cifrados, partindo do zero ou trocando os dois (data-model §1: 0 → 1 e
    /// 2 → 1). Como o +Vendas exige a senha em claro no login, quem troca o login troca a senha junto
    /// (contrato: "Informe a senha para trocar o login."), e a data exibida na tela é esta.
    /// </summary>
    public void SetCredentials(string loginEncrypted, string passwordEncrypted, DateTime now)
    {
        RequireCipher(loginEncrypted, nameof(loginEncrypted));
        RequireCipher(passwordEncrypted, nameof(passwordEncrypted));

        LoginEncrypted = loginEncrypted;
        PasswordEncrypted = passwordEncrypted;
        CredentialsSavedAt = now;
        PasswordChangedAt = now;
        CredentialsStatus = MaisVendasCredentialsStatus.Valid;
        UpdatedAt = now;
    }

    /// <summary>
    /// Troca só o login cifrado, reaproveitando a senha que já está guardada (data-model §1: 1 ou 2
    /// → 1). A senha não é reescrita e <see cref="PasswordChangedAt"/> não muda — ela não foi trocada.
    /// </summary>
    public void ChangeLogin(string loginEncrypted, DateTime now)
    {
        RequireCipher(loginEncrypted, nameof(loginEncrypted));

        if (!HasCredentials)
        {
            throw new InvalidOperationException("Não há credenciais do +Vendas para trocar o login.");
        }

        LoginEncrypted = loginEncrypted;
        CredentialsSavedAt = now;
        CredentialsStatus = MaisVendasCredentialsStatus.Valid;
        UpdatedAt = now;
    }

    /// <summary>
    /// Remove as credenciais (1 ou 2 → 0): zera as cinco colunas. O vínculo da conta
    /// (<see cref="AccountHash"/>) e o bloqueio por recusas não mudam — são outra regra (FR-006 da
    /// 008, edge case "remover as credenciais").
    /// </summary>
    public void ClearCredentials(DateTime now)
    {
        LoginEncrypted = null;
        PasswordEncrypted = null;
        CredentialsSavedAt = null;
        PasswordChangedAt = null;
        CredentialsStatus = MaisVendasCredentialsStatus.None;
        UpdatedAt = now;
    }

    private static void RequireCipher(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A credencial cifrada do +Vendas é obrigatória.", paramName);
        }
    }
}
