using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Imports;

/// <summary>
/// As nove contagens de uma execução ou de um bloco (feature 006, FR-047). São exatamente as colunas
/// <c>*_count</c> de <c>import_runs</c> e <c>import_run_blocks</c> (data-model §2), e por isso vivem
/// num único tipo: o resumo, o detalhe e o relatório por bloco mostram o mesmo feixe.
/// </summary>
public class ImportRunCountsInfo
{
    /// <summary>Pedidos criados.</summary>
    [JsonPropertyName("inserted")]
    public int Inserted { get; set; }

    /// <summary>Pedidos existentes que receberam os campos da origem (FR-023).</summary>
    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    /// <summary>Pedidos vistos na origem sem mudança de conteúdo (hash igual).</summary>
    [JsonPropertyName("unchanged")]
    public int Unchanged { get; set; }

    /// <summary>Pedidos importados antes e ausentes da origem nesta execução; nada foi apagado (FR-027).</summary>
    [JsonPropertyName("missing")]
    public int Missing { get; set; }

    /// <summary>Pedidos com dado inválido que não chegaram a ser gravados (FR-036).</summary>
    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    /// <summary>Pedidos manuais com a mesma chave natural, não sobrescritos (FR-028).</summary>
    [JsonPropertyName("conflicts")]
    public int Conflicts { get; set; }

    /// <summary>Clientes cadastrados sob demanda a partir de um CPF novo (FR-034).</summary>
    [JsonPropertyName("customersCreated")]
    public int CustomersCreated { get; set; }

    /// <summary>Fotos gravadas no storage da vendedora (FR-037/FR-038).</summary>
    [JsonPropertyName("imagesStored")]
    public int ImagesStored { get; set; }

    /// <summary>Fotos que falharam ou foram recusadas; o pedido continua gravado sem imagem (FR-039).</summary>
    [JsonPropertyName("imagesFailed")]
    public int ImagesFailed { get; set; }

    /// <summary>Todos os contadores em zero (execução recém-criada).</summary>
    public static ImportRunCountsInfo Empty() => new();
}
