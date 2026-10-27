using Ambrosio.DTO.Settings;

namespace Ambrosio.Application.Settings;

/// <summary>
/// Agregador de configuração da solução (research §16), montado pelo <c>Program.cs</c> a partir
/// de <c>ConnectionStrings:AmbrosioContext</c>, seção <c>Auth</c>, seção <c>CpfProtection</c> e
/// seção <c>ObjectStorage</c>.
/// As subclasses vivem no DTO porque são consumidas como <c>IOptions&lt;T&gt;</c> pela Infra.
/// </summary>
public class AmbrosioSettings
{
    /// <summary>Connection string do PostgreSQL (obrigatória: a API falha no startup se vazia).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Configuração do JWT Bearer.</summary>
    public AuthSettings Auth { get; set; } = new();

    /// <summary>Segredos de hash/cifra do CPF.</summary>
    public CpfProtectionSettings CpfProtection { get; set; } = new();

    /// <summary>Storage de imagens compatível com S3 (feature 004, research §12).</summary>
    public ObjectStorageSettings ObjectStorage { get; set; } = new();

    /// <summary>
    /// Importação do +Vendas (feature 006, research §13): qual coletor registrar, tamanho de bloco,
    /// limites de período/tentativa e ritmo da coleta. As faixas são conferidas no startup e
    /// <c>Collector=Fake</c> é recusado em Production.
    /// </summary>
    public MaisVendasSettings MaisVendas { get; set; } = new();
}
