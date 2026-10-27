using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports.Collector;

namespace Ambrosio.DTO.Imports;

/// <summary>
/// Resultado da decisão de upsert de um pedido importado (feature 006, research §6/§7). Tipo de
/// transporte interno entre o repositório e o serviço de execução — <b>não</b> é contrato de API e por
/// isso não tem <c>[JsonPropertyName]</c>.
/// </summary>
public sealed class UpsertOutcome
{
    /// <summary>O que foi feito com o pedido (FR-048).</summary>
    public ImportChangeAction Action { get; init; }

    /// <summary>
    /// Identificador do pedido no Ambrosio; <c>null</c> em simulação (nada foi gravado, FR-032) e
    /// quando a chave não resolveu em nada.
    /// </summary>
    public long? OrderId { get; init; }

    /// <summary>Nomes dos campos que mudaram, em <c>updated</c> (FR-023); <c>null</c> nas demais ações.</summary>
    public List<string>? ChangedFields { get; init; }

    /// <summary><c>true</c> quando um cliente novo foi criado por CPF (FR-034) — ou <i>seria</i> criado, em simulação.</summary>
    public bool CustomerCreated { get; init; }

    /// <summary>Quantas linhas do pedido ficaram ligadas a uma imagem já disponível (FR-037).</summary>
    public int ImagesLinked { get; init; }

    public static UpsertOutcome Inserted(long orderId, bool customerCreated) => new()
    {
        Action = ImportChangeAction.Inserted,
        OrderId = orderId,
        CustomerCreated = customerCreated
    };

    public static UpsertOutcome Unchanged(long orderId) => new()
    {
        Action = ImportChangeAction.Unchanged,
        OrderId = orderId
    };

    public static UpsertOutcome Updated(long orderId, List<string> changedFields) => new()
    {
        Action = ImportChangeAction.Updated,
        OrderId = orderId,
        ChangedFields = changedFields
    };

    public static UpsertOutcome Conflict(long orderId) => new()
    {
        Action = ImportChangeAction.Conflict,
        OrderId = orderId
    };
}

/// <summary>
/// Chave natural de um pedido já importado, dentro do intervalo de um bloco (feature 006, research §17).
/// Usada para reportar "não encontrado nesta execução" sem carregar conteúdo — e sem CPF nem nome de
/// cliente (FR-052).
/// </summary>
/// <param name="OrderId">Identificador do pedido no Ambrosio.</param>
/// <param name="OrderNumber">Número como foi gravado.</param>
/// <param name="OrderDate">Data do pedido.</param>
/// <param name="Kind">Tipo do pedido.</param>
/// <param name="Channel">Canal gravado, para o agrupamento do rastro.</param>
/// <param name="Origin">Origem gravada, para o agrupamento do rastro.</param>
public readonly record struct ImportedOrderKey(
    long OrderId,
    string OrderNumber,
    DateOnly OrderDate,
    OrderKind Kind,
    string? Channel,
    OrderOrigin? Origin);
