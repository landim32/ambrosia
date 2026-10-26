namespace Ambrosia.Domain.Models;

/// <summary>
/// Comprador atendido por <b>um</b> usuário (tabela <c>customers</c>). O mesmo CPF pode existir
/// para usuários diferentes (US4 cenário 4): a unicidade é <c>(user_id, cpf_hash)</c>.
/// O CPF nunca é armazenado em claro — apenas hash, cifra e máscara (FR-011).
/// </summary>
public class Customer
{
    public long Id { get; set; }

    /// <summary>Dono do cadastro. Vem exclusivamente do claim <c>sub</c> do token (FR-021).</summary>
    public long UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 hex (64 chars) dos 11 dígitos do CPF.</summary>
    public string CpfHash { get; set; } = string.Empty;

    /// <summary>AES-256-GCM em base64 (nonce || tag || ciphertext).</summary>
    public string CpfEncrypted { get; set; } = string.Empty;

    /// <summary>CPF mascarado usado em listagens: <c>***.456.789-**</c>.</summary>
    public string CpfMasked { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }

    public ICollection<Order> Orders { get; set; } = [];

    /// <summary>
    /// Muda apenas o nome (FR-009): as três colunas de CPF ficam intocadas, porque o documento é
    /// imutável nesta entrega — revalidá-lo exigiria recalcular a unicidade.
    /// </summary>
    /// <param name="name">Novo nome (o validator já garantiu 2-200 caracteres).</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    public void UpdateName(string name, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O nome do cliente é obrigatório.", nameof(name));
        }

        Name = name.Trim();
        UpdatedAt = now;
    }
}
