namespace Ambrosia.DTO.Settings;

/// <summary>
/// Configuração do storage de imagens compatível com S3 (research §12, FR-002, FR-020). Ligada à
/// seção <c>ObjectStorage</c> do appsettings e às variáveis <c>ObjectStorage__*</c>; consumida via
/// <c>IOptions&lt;ObjectStorageSettings&gt;</c> pela Infra (cliente S3), pelo <c>ImageService</c>
/// (limites e tipos aceitos) e pelo job de limpeza (prazos).
/// <para>
/// <see cref="AccessKey"/> e <see cref="SecretKey"/> são <b>segredos</b>: vêm do ambiente, nunca de
/// valor versionado, e nunca aparecem em log, mensagem de erro ou Swagger (FR-013, FR-021). As regras
/// de faixa são aplicadas no startup (<c>Program.ValidateSettings</c>), que recusa subir a API com
/// configuração incompleta (FR-020).
/// </para>
/// </summary>
public class ObjectStorageSettings
{
    /// <summary>Endpoint usado pela <b>API</b> para todas as operações (Put/Head/Get/Delete/List).</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint usado <b>só para assinar</b> as URLs pré-assinadas que o navegador vai chamar
    /// (research §3). Opcional: quando vazio, vale <see cref="ServiceUrl"/> — ver
    /// <see cref="EffectivePublicServiceUrl"/>. Necessário apenas quando a API alcança o storage por
    /// um endereço interno que o navegador não resolve.
    /// </summary>
    public string? PublicServiceUrl { get; set; }

    /// <summary>
    /// Região enviada na assinatura SigV4 (<c>AuthenticationRegion</c>). Em Spaces é o prefixo do
    /// endpoint (<c>nyc3</c>); em servidores S3 genéricos o padrão <c>us-east-1</c> funciona.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Nome do bucket, privado (FR-004). Nos Spaces o nome é global entre contas.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Endereçamento <c>host/bucket/chave</c> em vez de <c>bucket.host/chave</c>. Servidores
    /// auto-hospedados precisam de <c>true</c>; Spaces e S3 usam o padrão <c>false</c>.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>Chave de acesso (segredo). Obrigatória (research §12).</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Chave secreta (segredo). Obrigatória (research §12).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Validade das URLs pré-assinadas de leitura, em minutos (faixa 1-60, research §5).</summary>
    public int PresignedReadMinutes { get; set; } = 15;

    /// <summary>Validade das URLs pré-assinadas de envio, em minutos (faixa 1-15, research §4).</summary>
    public int PresignedUploadMinutes { get; set; } = 5;

    /// <summary>
    /// Tamanho máximo de um arquivo, em bytes (FR-005). Usado na validação da declaração, no
    /// limite do download da confirmação e no <c>[RequestSizeLimit]</c> do endpoint multipart.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 10_485_760;

    /// <summary>
    /// Tipos aceitos (FR-005). Vazia por padrão no objeto — quem preenche é o appsettings, para que
    /// a recusa de um tipo não suportado seja decisão de configuração, não de código. Só podem
    /// entrar tipos com extensão mapeada na chave do objeto (research §12).
    /// </summary>
    public List<string> AllowedMimeTypes { get; set; } = [];

    /// <summary>
    /// idade de uma imagem <c>pending</c>/<c>failed</c> antes de o job de limpeza removê-la, em
    /// minutos (FR-018; faixa 1-1440).
    /// </summary>
    public int PendingTtlMinutes { get; set; } = 60;

    /// <summary>Intervalo entre varreduras do job de limpeza, em minutos (faixa 1-60).</summary>
    public int CleanupIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Endpoint que as URLs pré-assinadas devem usar: <see cref="PublicServiceUrl"/> quando
    /// informada, <see cref="ServiceUrl"/> caso contrário (research §3).
    /// </summary>
    public string EffectivePublicServiceUrl =>
        string.IsNullOrWhiteSpace(PublicServiceUrl) ? ServiceUrl : PublicServiceUrl;
}
