using System.Reflection;
using Ambrosio.Infra.Interfaces.Repository;

namespace Ambrosio.Tests.Infra.Repository;

/// <summary>
/// T087 [US4]: prova estrutural do isolamento (FR-021, FR-022, research §13). O isolamento não é
/// um acordo no corpo de cada repositório — é cobrado pela assinatura:
/// <list type="number">
/// <item>todo método de leitura de cliente/pedido recebe <c>long userId</c>, então não existe
/// consulta "sem filtro" que alguém possa esquecer de usar; e</item>
/// <item>nenhum DTO de entrada tem propriedade <c>UserId</c>, então o corpo da requisição não tem
/// como escolher o dono dos dados.</item>
/// </list>
/// As únicas exceções são <c>InsertAsync</c>/<c>UpdateAsync</c>, que recebem a entidade obtida por
/// uma leitura já filtrada.
/// </summary>
public class RepositoryIsolationContractTest
{
    /// <summary>Métodos de escrita: operam sobre a entidade vinda de uma leitura filtrada.</summary>
    private static readonly string[] WriteMethods = ["InsertAsync", "UpdateAsync"];

    [Theory]
    [InlineData(typeof(ICustomerRepository<>))]
    [InlineData(typeof(IOrderRepository<>))]
    public void EveryReadMethod_ShouldRequireALongUserIdParameter(Type repositoryInterface)
    {
        // Act
        var offenders = repositoryInterface
            .GetMethods()
            .Where(m => !WriteMethods.Contains(m.Name))
            .Where(m => !m.GetParameters().Any(p => p.Name == "userId" && p.ParameterType == typeof(long)))
            .Select(m => m.Name)
            .ToList();

        // Assert
        Assert.True(
            offenders.Count == 0,
            $"{repositoryInterface.Name}: leitura sem o parâmetro `long userId` -> {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData(typeof(ICustomerRepository<>))]
    [InlineData(typeof(IOrderRepository<>))]
    public void WriteMethods_ShouldNotAcceptARawUserIdFromTheCaller(Type repositoryInterface)
    {
        // A escrita recebe a ENTIDADE, não um id de usuário solto: quem escreve já passou por
        // GetByIdAsync(id, userId). Um `long userId` aqui seria um convite para sobrescrecer o dono.
        var offenders = repositoryInterface
            .GetMethods()
            .Where(m => WriteMethods.Contains(m.Name))
            .Where(m => m.GetParameters().Length != 1)
            .Select(m => m.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{repositoryInterface.Name}: método de escrita com mais de um parâmetro -> {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData("Ambrosio.DTO.Customers")]
    [InlineData("Ambrosio.DTO.Orders")]
    public void NoInputDto_ShouldExposeAUserId(string namespaze)
    {
        // Arrange: DTOs de entrada terminam em InsertInfo / UpdateInfo / ListQuery.
        var inputDtos = typeof(Ambrosio.DTO.Customers.CustomerInfo).Assembly
            .GetTypes()
            .Where(t => t.Namespace == namespaze)
            .Where(t => t.Name.EndsWith("InsertInfo", StringComparison.Ordinal)
                        || t.Name.EndsWith("UpdateInfo", StringComparison.Ordinal)
                        || t.Name.EndsWith("ListQuery", StringComparison.Ordinal))
            .ToList();

        // Assert: o conjunto existe (senão o teste passaria por vacuidade).
        Assert.True(inputDtos.Count > 0, $"nenhum DTO de entrada encontrado em {namespaze}");

        foreach (var dto in inputDtos)
        {
            var offender = dto.GetProperty(
                "UserId",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            Assert.True(offender is null,
                $"{dto.Name} expõe {nameof(offender)}: o dono dos dados vem apenas do claim `sub` (FR-021)");
        }
    }

    [Theory]
    [InlineData("Ambrosio.DTO.Customers")]
    [InlineData("Ambrosio.DTO.Orders")]
    public void EveryInputShape_ShouldBeCoveredByTheNamingRule(string namespaze)
    {
        // Guarda da checagem acima: se alguém criar um DTO de entrada sem sufixo InsertInfo /
        // UpdateInfo / ListQuery, ele escapa da varredura de `userId`. Este teste mantém a lacuna
        // visível em vez de deixar o teste anterior passar em falso.
        var uncovered = typeof(Ambrosio.DTO.Customers.CustomerInfo).Assembly
            .GetTypes()
            .Where(t => t.Namespace == namespaze)
            .Where(t => t.Name.EndsWith("Info", StringComparison.Ordinal)
                        || t.Name.EndsWith("Query", StringComparison.Ordinal))
            .Where(t => !IsInputShape(t.Name) && !IsOutputShape(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.True(uncovered.Count == 0,
            $"shapes nem de entrada nem de saída reconhecidos por sufixo -> {string.Join(", ", uncovered)}");
    }

    [Fact]
    public void OrderItemInsertInfo_ShouldNotAcceptALineNo()
    {
        // FR-018: a numeração é atribuída por Order.AddItem. Aceitar lineNo na entrada permitiria
        // linhas duplicadas dentro do mesmo pedido (índice ux_order_items_order_line).
        var offender = typeof(Ambrosio.DTO.Orders.OrderItemInsertInfo)
            .GetProperty("LineNo", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        Assert.Null(offender);
    }

    [Fact]
    public void CustomerInfo_ShouldNotHaveAPlaceForTheFullCpf()
    {
        // SC-005: o tipo usado em listagem e dentro de OrderDetailInfo.customer não tem propriedade
        // para o CPF completo — o vazamento é impossível por construção, não por convenção.
        var offender = typeof(Ambrosio.DTO.Customers.CustomerInfo)
            .GetProperty("Cpf", BindingFlags.Public | BindingFlags.Instance);

        Assert.Null(offender);
    }

    private static bool IsInputShape(string name) =>
        name.EndsWith("InsertInfo", StringComparison.Ordinal)
        || name.EndsWith("UpdateInfo", StringComparison.Ordinal)
        || name.EndsWith("ListQuery", StringComparison.Ordinal);

    /// <summary>Shapes de saída conhecidos (data-model, tabela "Mapeamento DTO ↔ Modelo").</summary>
    private static bool IsOutputShape(string name) =>
        name.EndsWith("DetailInfo", StringComparison.Ordinal)
        || name.EndsWith("SummaryInfo", StringComparison.Ordinal)
        || name is "CustomerInfo" or "OrderInfo" or "OrderItemInfo";
}
