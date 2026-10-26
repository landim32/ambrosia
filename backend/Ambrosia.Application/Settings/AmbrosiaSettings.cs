using Ambrosia.DTO.Settings;

namespace Ambrosia.Application.Settings;

/// <summary>
/// Agregador de configuração da solução (research §16), montado pelo <c>Program.cs</c> a partir
/// de <c>ConnectionStrings:AmbrosiaContext</c>, seção <c>Auth</c> e seção <c>CpfProtection</c>.
/// As subclasses vivem no DTO porque são consumidas como <c>IOptions&lt;T&gt;</c> pela Infra.
/// </summary>
public class AmbrosiaSettings
{
    /// <summary>Connection string do PostgreSQL (obrigatória: a API falha no startup se vazia).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Configuração do JWT Bearer.</summary>
    public AuthSettings Auth { get; set; } = new();

    /// <summary>Segredos de hash/cifra do CPF.</summary>
    public CpfProtectionSettings CpfProtection { get; set; } = new();
}
