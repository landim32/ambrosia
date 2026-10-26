using System.Security.Cryptography;
using Ambrosia.DTO.Customers;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Orders;

namespace Ambrosia.ApiTests.Helpers;

/// <summary>
/// Geração de dados de teste (skill <c>dotnet-test-api</c>): CPFs válidos aleatórios e payloads
/// de cliente/pedido. Cada execução cria documentos e números novos, então os testes não brigam
/// com dados de execuções anteriores no banco de desenvolvimento.
/// </summary>
internal static class TestDataHelper
{
    /// <summary>CPF de referência do quickstart (válido), com pontuação.</summary>
    public const string QUICKSTART_CPF = "529.982.247-25";

    /// <summary>
    /// O mesmo documento sem pontuação: a unicidade é por CPF normalizado (FR-010), então cadastrar
    /// esta forma sobre <see cref="QUICKSTART_CPF"/> deve dar 409.
    /// </summary>
    public const string QUICKSTART_CPF_DIGITS = "52998224725";

    /// <summary>Total da venda de exemplo do doc 01 / Cenário 3 do quickstart.</summary>
    public const decimal SALE_TOTAL = 589.80m;

    /// <summary>Data da venda de exemplo (ontem em relação ao "hoje" do quickstart).</summary>
    public static readonly DateOnly SaleDate = new(2026, 10, 24);

    /// <summary>
    /// CPF brasileiro aleatório e matematicamente válido: 9 dígitos fortuitos + 2 verificadores
    /// calculados por módulo 11, formatado como <c>000.000.000-00</c>.
    /// </summary>
    public static string GenerateValidCpf()
    {
        var digits = new int[11];

        // Sequências repetidas (111.111.111-11) são rejeitadas pela regra de negócio (research §5):
        // sorteia de novo até ter pelo menos dois dígitos diferentes.
        do
        {
            for (var i = 0; i < 9; i++)
            {
                digits[i] = RandomNumberGenerator.GetInt32(0, 10);
            }
        }
        while (digits[..9].Distinct().Count() < 2);

        digits[9] = ComputeFirstCheckDigit(digits);
        digits[10] = ComputeSecondCheckDigit(digits);

        var raw = new string(digits.Select(d => (char)('0' + d)).ToArray());

        return $"{raw[..3]}.{raw[3..6]}.{raw[6..9]}-{raw[9..]}";
    }

    /// <summary>Cliente inédito para <c>POST /customers</c>.</summary>
    public static CustomerInsertInfo NewCustomer(string? name = null) => new()
    {
        Name = name ?? $"Cliente {Guid.NewGuid().ToString("N")[..8]}",
        Cpf = GenerateValidCpf()
    };

    /// <summary>
    /// Venda do quickstart: R$ 589,80 com "Calça Inez" (42CA500UIE_12182, tam. 38, R$ 399,90) e
    /// "Blusa Lia" (41BL200AAA_00001, tam. P, R$ 189,90), canal Linx, origem cota, status
    /// "Faturado" e número único por execução (T075).
    /// </summary>
    public static OrderInsertInfo NewSaleOrder(long customerId, string? orderNumber = null) => new()
    {
        CustomerId = customerId,
        OrderNumber = orderNumber ?? NewOrderNumber(),
        OrderDate = SaleDate,
        Kind = OrderKind.Sale,
        Channel = "Linx",
        Origin = OrderOrigin.Cota,
        Status = "Faturado",
        Total = SALE_TOTAL,
        Items =
        [
            new OrderItemInsertInfo
            {
                ProductName = "Calça Inez",
                SkuRef = "42CA500UIE_12182",
                Size = "38",
                Price = 399.90m
            },
            new OrderItemInsertInfo
            {
                ProductName = "Blusa Lia",
                SkuRef = "41BL200AAA_00001",
                Size = "P",
                Price = 189.90m
            }
        ]
    };

    /// <summary>
    /// Cancelamento do quickstart: total negativo, mesma data da venda original e item com preço
    /// negativo (SC-006 — nada de informação é perdido).
    /// </summary>
    /// <param name="customerId">Cliente dono do pedido cancelado.</param>
    /// <param name="orderNumber">Número do pedido vendido que se cancela (a unicidade é por
    /// número + data + tipo, então repetir o número com kind diferente é justamente o caso).</param>
    public static OrderInsertInfo NewCancellationOrder(long customerId, string orderNumber) => new()
    {
        CustomerId = customerId,
        OrderNumber = orderNumber,
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Kind = OrderKind.Cancellation,
        Channel = "Linx",
        Status = "Cancelado",
        Total = -SALE_TOTAL,
        OriginalSaleOn = SaleDate,
        CancelledOn = DateOnly.FromDateTime(DateTime.UtcNow),
        Items =
        [
            new OrderItemInsertInfo
            {
                ProductName = "Calça Inez",
                SkuRef = "42CA500UIE_12182",
                Size = "38",
                Price = -399.90m
            }
        ]
    };

    /// <summary>Número de pedido inédito no formato do +Vendas (<c>v1161148crb-01</c>).</summary>
    public static string NewOrderNumber() =>
        $"v{Guid.NewGuid().ToString("N")[..10].ToLowerInvariant()}-01";

    private static int ComputeFirstCheckDigit(int[] digits)
    {
        var sum = 0;
        var weight = 10;

        for (var i = 0; i < 9; i++)
        {
            sum += digits[i] * weight--;
        }

        return ToDigit(sum);
    }

    private static int ComputeSecondCheckDigit(int[] digits)
    {
        var sum = 0;
        var weight = 11;

        for (var i = 0; i < 10; i++)
        {
            sum += digits[i] * weight--;
        }

        return ToDigit(sum);
    }

    /// <summary>Módulo 11: resto 0 ou 1 vira dígito 0 (algoritmo clássico do CPF).</summary>
    private static int ToDigit(int sum)
    {
        var remainder = sum % 11;

        return remainder < 2 ? 0 : 11 - remainder;
    }
}
