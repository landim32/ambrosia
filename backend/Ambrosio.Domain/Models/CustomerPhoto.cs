using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Models;

/// <summary>
/// Uma foto do álbum de uma cliente (feature 009, tabela <c>customer_photos</c>, data-model §1).
/// <b>Uma linha = um arquivo</b>: o Ambrosio não decodifica nem regrava imagem no servidor, então não
/// existem aqui colunas de miniatura nem de recorte (research R2/R3). A legenda, a favorita e a ordem
/// da grade são as únicas variações que este registro carrega.
/// <para>
/// O dono (<see cref="UserId"/>) é sempre o da cliente e vem exclusivamente do claim <c>sub</c>
/// (constituição §VI): nenhum corpo de requisição transporta usuário.
/// </para>
/// </summary>
public class CustomerPhoto
{
    public long Id { get; set; }

    /// <summary>Dono da foto; igual ao <c>user_id</c> da cliente (FR-025).</summary>
    public long UserId { get; set; }

    public long CustomerId { get; set; }

    /// <summary>Arquivo no storage privado (feature 004). Sempre <c>available</c> — conferido no serviço.</summary>
    public long ImageId { get; set; }

    /// <summary>Legenda até 500 caracteres; <c>null</c> quando vazia (FR-007).</summary>
    public string? Description { get; set; }

    /// <summary>Coração do álbum (FR-008). Independent e reversível a qualquer momento.</summary>
    public bool IsFavorite { get; set; }

    public CustomerPhotoSource Source { get; set; }

    /// <summary>UTC. É a ordem da grade: mais recente primeiro (FR-010).</summary>
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Customer? Customer { get; set; }

    public StoredImage? StoredImage { get; set; }

    /// <summary>Navegação da FK <c>fk_user_customer_photo</c>; é o que impede o EF de criar coluna sombra.</summary>
    public User? User { get; set; }

    /// <summary>
    /// Cria a foto do álbum apontando para uma imagem já disponível. A favorita nasce <c>false</c>:
    /// marcar coração é decisão posterior da vendedora, nunca efeito de envio (cenário US6-1).
    /// </summary>
    /// <param name="description">Texto bruto; é despido de espaços e o vazio vira <c>null</c>.</param>
    public static CustomerPhoto Create(
        long userId,
        long customerId,
        long imageId,
        string? description,
        CustomerPhotoSource source,
        DateTime now) => new()
    {
        UserId = userId,
        CustomerId = customerId,
        ImageId = imageId,
        Description = Normalize(description),
        IsFavorite = false,
        Source = source,
        CreatedAt = now,
        UpdatedAt = now
    };

    /// <summary>FR-007: a descrição pode ser criada, trocada e apagada a qualquer momento.</summary>
    public void UpdateDescription(string? description, DateTime now)
    {
        var normalized = Normalize(description);

        // Reescrever a mesma legenda não é mudança: atualizar updated_at embaralharia a ordem da
        // "última alteração" que a vendedora vê no visualizador.
        if (normalized == Description)
        {
            return;
        }

        Description = normalized;
        UpdatedAt = now;
    }

    /// <summary>FR-008: ligar e desligar o coração, idempotente (a conversa pode repetir o pedido).</summary>
    public void SetFavorite(bool favorite, DateTime now)
    {
        if (IsFavorite == favorite)
        {
            return;
        }

        IsFavorite = favorite;
        UpdatedAt = now;
    }

    /// <summary>Legenda vazia ou só espaços é <c>null</c> — "sem descrição" é um estado, não texto vazio.</summary>
    private static string? Normalize(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
