using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Models;

/// <summary>
/// Imagem armazenada no storage compatível com S3 (tabela <c>stored_images</c>). O binário vive no
/// bucket privado, identificado por <see cref="StorageKey"/>; esta linha é o metadado que autoriza
/// acessá-lo. O dono (<see cref="UserId"/>) nunca vem de um DTO: é o claim <c>sub</c> do token
/// (FR-014), e a unicidade <c>(user_id, sha256)</c> com status ativo é o que deduplica (FR-011).
/// <para>
/// Os estados e suas transições estão no data-model: <c>pending → available</c> (confirmação que
/// confere), <c>pending → failed</c> (confirmação que recusa, com o objeto apagado) e as saídas por
/// exclusão ou limpeza. <c>available</c> e <c>failed</c> são finais.
/// </para>
/// </summary>
public class StoredImage
{
    public long Id { get; set; }

    /// <summary>Dono da imagem. Vem exclusivamente do claim <c>sub</c> do token (FR-014).</summary>
    public long UserId { get; set; }

    /// <summary>Chave do objeto no bucket, no formato de <see cref="BuildStorageKey"/>.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>SHA-256 em hex minúsculo (64 chars) do binário que está no storage.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Largura em pixels, lida do cabeçalho; nula enquanto não se olhou o conteúdo.</summary>
    public int? Width { get; set; }

    /// <summary>Altura em pixels; mesmo contrato de <see cref="Width"/>.</summary>
    public int? Height { get; set; }

    public ImageStatus Status { get; set; }

    public ImageSource Source { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Envio declarado pelo cliente: o binário ainda pode não estar no storage, e só a confirmação
    /// decide se vira <c>available</c> ou <c>failed</c> (FR-003, FR-006).
    /// </summary>
    /// <param name="userId">Dono, do claim <c>sub</c>.</param>
    /// <param name="sha256">Hash já normalizado (hex minúsculo).</param>
    /// <param name="mimeType">Tipo já normalizado (minúsculas).</param>
    /// <param name="sizeBytes">Tamanho declarado; conferido no <c>HeadObject</c> da confirmação.</param>
    /// <param name="source">Origem do binário.</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    public static StoredImage CreatePending(
        long userId,
        string sha256,
        string mimeType,
        long sizeBytes,
        ImageSource source,
        DateTime now) => new()
        {
            UserId = userId,
            Sha256 = sha256,
            MimeType = mimeType,
            SizeBytes = sizeBytes,
            StorageKey = BuildStorageKey(userId, sha256, mimeType),
            Status = ImageStatus.Pending,
            Source = source,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>
    /// Imagem gravada e validada pelo próprio servidor (FR-008, FR-009): nasce direto em
    /// <c>available</c>, porque tamanho, tipo e hash já foram conferidos antes de subir.
    /// </summary>
    /// <param name="userId">Dono, do claim <c>sub</c>.</param>
    /// <param name="sha256">Hash do conteúdo, em hex minúsculo.</param>
    /// <param name="mimeType">Tipo informado, já conferido contra os aceitos.</param>
    /// <param name="sizeBytes">Tamanho do conteúdo.</param>
    /// <param name="width">Largura em pixels, quando conhecida.</param>
    /// <param name="height">Altura em pixels, quando conhecida.</param>
    /// <param name="source">Origem do binário.</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    public static StoredImage CreateAvailable(
        long userId,
        string sha256,
        string mimeType,
        long sizeBytes,
        int? width,
        int? height,
        ImageSource source,
        DateTime now) => new()
        {
            UserId = userId,
            Sha256 = sha256,
            MimeType = mimeType,
            SizeBytes = sizeBytes,
            Width = width,
            Height = height,
            StorageKey = BuildStorageKey(userId, sha256, mimeType),
            Status = ImageStatus.Available,
            Source = source,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>
    /// Confirmação aprovada: a imagem passa a ter URL de leitura (FR-006).
    /// </summary>
    /// <param name="width">Largura lida do cabeçalho do binário; pode ser nula.</param>
    /// <param name="height">Altura lida do cabeçalho do binário; pode ser nula.</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    /// <exception cref="InvalidOperationException">Fora de <see cref="ImageStatus.Pending"/> — <c>available</c> e <c>failed</c> são finais.</exception>
    public void MarkAvailable(int? width, int? height, DateTime now)
    {
        ThrowIfNotPending();

        Width = width;
        Height = height;
        Status = ImageStatus.Available;
        UpdatedAt = now;
    }

    /// <summary>
    /// Confirmação recusada por tamanho, tipo ou hash. O chamador apaga o objeto do storage antes
    /// ou depois desta chamada; o status <c>failed</c> tira a linha da unicidade parcial, então o
    /// mesmo conteúdo pode ser declarado de novo (clarificação 5 da spec).
    /// </summary>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    /// <exception cref="InvalidOperationException">Fora de <see cref="ImageStatus.Pending"/>.</exception>
    public void MarkFailed(DateTime now)
    {
        ThrowIfNotPending();

        Status = ImageStatus.Failed;
        UpdatedAt = now;
    }

    /// <summary>Pasta raiz do bucket onde ficam todos os objetos da aplicação.</summary>
    public const string StorageRootFolder = "ambrosio";

    /// <summary>
    /// Prefixo de todos os objetos de um usuário: <c>ambrosio/users/{userId}/</c>. Usado na chave
    /// canônica e na exclusão em massa da conta (FR-017).
    /// </summary>
    /// <param name="userId">Dono das imagens.</param>
    public static string BuildUserPrefix(long userId) =>
        $"{StorageRootFolder}/users/{userId}/";

    /// <summary>
    /// Chave canônica do objeto: <c>ambrosio/users/{userId}/images/{sha256}.{ext}</c> (FR-016). Ela
    /// é determinística pelo conteúdo, o que dá a deduplicação física, e prefixada pelo dono, o que
    /// permite apagar todas as imagens de um usuário de uma vez (FR-017).
    /// </summary>
    /// <param name="userId">Dono da imagem.</param>
    /// <param name="sha256">Hash em hex minúsculo.</param>
    /// <param name="mimeType">Tipo aceito (ver <see cref="ExtensionByMimeType"/>).</param>
    public static string BuildStorageKey(long userId, string sha256, string mimeType) =>
        $"{BuildUserPrefix(userId)}images/{sha256}.{ExtensionByMimeType(mimeType)}";

    private static readonly Dictionary<string, string> ExtensionByMimeTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp"
    };

    private static string ExtensionByMimeType(string mimeType)
    {
        if (!ExtensionByMimeTypeMap.TryGetValue(mimeType, out var extension))
        {
            throw new ArgumentException(
                $"Não há extensão mapeada para o tipo '{mimeType}'.", nameof(mimeType));
        }

        return extension;
    }

    private void ThrowIfNotPending()
    {
        if (Status != ImageStatus.Pending)
        {
            throw new InvalidOperationException(
                $"A imagem {Id} está em status {Status}; só um envio pendente muda de estado.");
        }
    }
}
