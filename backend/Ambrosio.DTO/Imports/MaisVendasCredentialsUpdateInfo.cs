using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Imports;

/// <summary>
/// Corpo do <c>PUT /imports/mais-vendas/credentials</c> (feature 008, US1, data-model §3): o login e a
/// senha que a vendedora acabou de digitar na tela de configuração.
/// <para>
/// Os dois campos são <b>opcionais no transporte</b> de propósito: <c>null</c> significa "não mexi
/// nisso", e a regra de quais combinações são aceitas fica no validator
/// (<c>MaisVendasCredentialsUpdateInfoValidator</c>) e no serviço, não no binding. É o que permite
/// trocar só a senha sem reescrever o login.
/// </para>
/// <para>
/// Assim como em <see cref="ImportRunStartInfo"/>, o valor entra aqui, é cifrado e morre: o DTO nunca é
/// ecoado em resposta, log ou auditoria (FR-003), e <c>format: password</c> mantém os dois campos fora
/// do exemplo gerado no Swagger — o login também, porque ele identifica uma conta real num serviço de
/// terceiro.
/// </para>
/// </summary>
public class MaisVendasCredentialsUpdateInfo
{
    /// <summary>Login do +Vendas (em homolog, <c>conta+cenario</c>). <c>null</c> = mantém o gravado.</summary>
    [JsonPropertyName("login")]
    [DataType(DataType.Password)]
    public string? Login { get; set; }

    /// <summary>
    /// Senha do +Vendas em claro, apenas para cifra imediata na transação do cadastro.
    /// <c>null</c> = mantém a senha já gravada.
    /// </summary>
    [JsonPropertyName("password")]
    [DataType(DataType.Password)]
    public string? Password { get; set; }
}
