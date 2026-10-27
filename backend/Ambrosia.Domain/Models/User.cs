using Ambrosia.DTO.Enums;

namespace Ambrosia.Domain.Models;

/// <summary>
/// Pessoa que acessa a API (tabela <c>users</c>). Criada automaticamente no primeiro login
/// OAuth ou no cadastro com e-mail e senha. A senha nunca é exposta em DTO (FR-003, FR-005).
/// </summary>
public class User
{
    public long Id { get; set; }

    /// <summary>E-mail normalizado (<see cref="NormalizeEmail"/>): minúsculas, sem espaços nas pontas.</summary>
    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Hash autodescritivo da senha (PBKDF2-HMAC-SHA256 — research §1). <c>null</c> = conta sem
    /// senha (Google, ou usuário antigo da entrada de desenvolvimento). NUNCA mapeado para DTO.
    /// </summary>
    public string? PasswordHash { get; set; }

    public AuthProvider Provider { get; set; }

    /// <summary>Claim <c>sub</c> retornado pelo provedor (Google).</summary>
    public string? ProviderSubject { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Customer> Customers { get; set; } = [];

    /// <summary>
    /// Imagens da conta no storage (feature 004). A navegação existe para o EF mapear a FK
    /// <c>fk_user_stored_image</c> com <c>ClientSetNull</c>: excluir o usuário não apaga linha nem
    /// objeto por cascata — a remoção das imagens é explícita (<c>IImageService.DeleteAllForUserAsync</c>,
    /// FR-017).
    /// </summary>
    public ICollection<StoredImage> StoredImages { get; set; } = [];

    /// <summary>
    /// A conta tem senha? <c>false</c> significa que ela só entra pelo Google (e que o login por
    /// senha cai no 401 genérico — FR-008).
    /// </summary>
    public bool HasPassword => PasswordHash is not null;

    /// <summary>
    /// Define o hash derivado da senha. Chamado só no cadastro (FR-001): não existe troca de
    /// senha nesta feature (Assumptions da spec).
    /// </summary>
    /// <param name="passwordHash">Hash autodescritivo vindo de <c>IPasswordHashAppService.Hash</c>.</param>
    /// <param name="now">Instante UTC do relógio injetado.</param>
    public void SetPassword(string passwordHash, DateTime now)
    {
        PasswordHash = passwordHash;
        UpdatedAt = now;
    }

    /// <summary>
    /// Remove a senha da conta (FR-009): quando o Google entra num e-mail que tinha senha, a
    /// senha deixa de existir — mantém o invariante <c>Provider = Google ⇒ PasswordHash = NULL</c>.
    /// </summary>
    /// <param name="now">Instante UTC do relógio injetado.</param>
    public void ClearPassword(DateTime now)
    {
        PasswordHash = null;
        UpdatedAt = now;
    }

    /// <summary>Marca a conta como acabada de usar (login com senha bem-sucedido — data-model §1).</summary>
    /// <param name="now">Instante UTC do relógio injetado.</param>
    public void Touch(DateTime now) => UpdatedAt = now;

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
