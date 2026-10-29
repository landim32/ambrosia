namespace Ambrosio.Domain.Models;

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
    /// Foto do álbum de onde saiu a foto de perfil (feature 009, FR-014). <c>null</c> = sem perfil,
    /// e o avatar continua mostrando as iniciais (FR-016, cenário US3-5).
    /// </summary>
    public long? ProfilePhotoId { get; set; }

    /// <summary>
    /// Recorte quadrado de até 512 × 512 <b>gerado no navegador</b> da vendedora (research R4).
    /// <c>null</c> quando o perfil foi definido por um canal sem navegador — conversa ou API sem
    /// recorte — e o círculo passa a usar o arquivo da própria foto (FR-013).
    /// </summary>
    public long? ProfileImageId { get; set; }

    public CustomerPhoto? ProfilePhoto { get; set; }

    public StoredImage? ProfileImage { get; set; }

    /// <summary>
    /// Define a foto de perfil (FR-011): no máximo uma por cliente, e escolher outra substitui a
    /// anterior — por isso o recorte velho é devolvido ao chamador, que decide se ele ficou órfão.
    /// </summary>
    /// <param name="photoId">Foto do álbum; a pertinência à cliente é conferida no serviço.</param>
    /// <param name="profileImageId">Recorte do navegador, ou <c>null</c> para usar o arquivo da foto.</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    /// <returns>Id do recorte que estava no perfil antes, se havia (candidato a órfão).</returns>
    public long? SetProfilePhoto(long photoId, long? profileImageId, DateTime now)
    {
        var previousImageId = ProfileImageId;

        ProfilePhotoId = photoId;
        ProfileImageId = profileImageId;
        UpdatedAt = now;

        return previousImageId;
    }

    /// <summary>
    /// Remove o perfil sem tocar no álbum (FR-015). Devolve o recorte para a mesma checagem de órfão
    /// de <see cref="SetProfilePhoto"/>.
    /// </summary>
    public long? ClearProfilePhoto(DateTime now)
    {
        var previousImageId = ProfileImageId;

        ProfilePhotoId = null;
        ProfileImageId = null;
        UpdatedAt = now;

        return previousImageId;
    }

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
