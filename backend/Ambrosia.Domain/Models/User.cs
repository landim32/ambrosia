using Ambrosia.DTO.Enums;

namespace Ambrosia.Domain.Models;

/// <summary>
/// Pessoa que acessa a API (tabela <c>users</c>). Criada automaticamente no primeiro login
/// OAuth ou dev-login. A senha nunca é exposta em DTO (FR-003).
/// </summary>
public class User
{
    public long Id { get; set; }

    /// <summary>E-mail normalizado (<see cref="NormalizeEmail"/>): minúsculas, sem espaços nas pontas.</summary>
    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Só existe em cadastro local (fora do escopo). NUNCA mapeado para DTO.</summary>
    public string? PasswordHash { get; set; }

    public AuthProvider Provider { get; set; }

    /// <summary>Claim <c>sub</c> retornado pelo provedor (Google).</summary>
    public string? ProviderSubject { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Customer> Customers { get; set; } = [];

    /// <summary>
    /// Atualiza o cadastro com os dados mais recentes do provedor no login seguinte: o nome pode
    /// ter mudado lá (e chega vazio em alguns tokens), e o <c>sub</c> é a âncora de identidade
    /// (data-model: <c>updated_at</c> é atualizado a cada login).
    /// </summary>
    /// <param name="name">Nome declarado pelo provedor; quando vazio, mantém o nome atual.</param>
    /// <param name="subject">Claim <c>sub</c> do provedor (<c>null</c> preserva o valor anterior).</param>
    /// <param name="now">Instante UTC do relógio injetado (<see cref="Interfaces.IClock"/>).</param>
    public void UpdateFromProvider(string name, string? subject, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            ProviderSubject = subject;
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// Forma canônica do e-mail usada como chave de busca e como índice único
    /// <c>ux_users_email</c>: minúsculas e sem espaços nas pontas.
    /// </summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
