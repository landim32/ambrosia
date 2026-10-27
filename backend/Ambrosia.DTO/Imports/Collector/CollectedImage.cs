namespace Ambrosia.DTO.Imports.Collector;

/// <summary>
/// Uma foto baixada da origem com a sessão da coleta (feature 006, research §9/§12).
/// <para>
/// O conteúdo é entregue como <see cref="Stream"/> para o pipeline de fotos passá-lo direto ao
/// <c>IImageService.StoreFromServerAsync</c>, que calcula hash, aplica a deduplicação e grava no
/// storage da vendedora. O download é limitado a <c>ObjectStorage:MaxUploadBytes</c> antes de virar
/// memória: <see cref="SizeBytes"/> é o tamanho anunciado pela origem e é conferido.
/// </para>
/// </summary>
public sealed class CollectedImage : IAsyncDisposable
{
    public required Stream Content { get; init; }

    /// <summary><c>Content-Type</c> da resposta — é o tipo que a política de aceitação do storage confere.</summary>
    public required string MimeType { get; init; }

    /// <summary>Tamanho em bytes (anunciado pela origem, conferido na leitura limitada).</summary>
    public required long SizeBytes { get; init; }

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
