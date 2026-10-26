namespace Ambrosia.ApiTests.Fixtures;

/// <summary>Definição da coleção compartilhada (xUnit: um fixture por coleção, serial).</summary>
[CollectionDefinition(API_TESTS)]
public class ApiTestCollection : ICollectionFixture<ApiTestFixture>
{
    /// <summary>Nome usado em <c>[Collection(ApiTestCollection.API_TESTS)]</c> nas classes de teste.</summary>
    public const string API_TESTS = "ApiTests";
}
