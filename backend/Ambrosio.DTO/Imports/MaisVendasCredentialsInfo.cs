using System.Text.Json.Serialization;
using Ambrosio.DTO.Enums;

namespace Ambrosio.DTO.Imports;

/// <summary>
/// Resposta das três rotas de credenciais do +Vendas — <c>GET</c>, <c>PUT</c> e <c>DELETE</c>
/// <c>/imports/mais-vendas/credentials</c> (feature 008, US1, FR-001/FR-003).
/// <para>
/// <b>Não existe campo de senha aqui</b>, nem mascarado: <see cref="HasPassword"/> diz se há senha
/// guardada sem dizer qual é, e é por isso que a tela consegue mostrar "Senha cadastrada em 28/10/2026"
/// sem nunca ter recebido a senha (FR-003, SC-003). O <see cref="Login"/> volta inteiro porque é dado da
/// própria vendedora sobre um serviço de terceiro, e ela precisa dele para editar o cadastro; nem por
/// isso a senha acompanha a troca de login.
/// </para>
/// </summary>
public class MaisVendasCredentialsInfo
{
    /// <summary>Login guardado, em claro; string vazia quando não há credenciais.</summary>
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    /// <summary>Há senha cifrada guardada? Nunca o valor da senha.</summary>
    [JsonPropertyName("hasPassword")]
    public bool HasPassword { get; set; }

    /// <summary>Última gravação de login ou senha (UTC); <c>null</c> sem credenciais.</summary>
    [JsonPropertyName("savedAt")]
    public DateTime? SavedAt { get; set; }

    /// <summary>Última troca de senha (UTC) — a data que a tela exibe; <c>null</c> sem credenciais.</summary>
    [JsonPropertyName("passwordChangedAt")]
    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>
    /// Situação: <c>none</c> | <c>valid</c> | <c>needsUpdate</c> (FR-004/FR-007).
    /// </summary>
    [JsonPropertyName("status")]
    public MaisVendasCredentialsStatus Status { get; set; }
}
