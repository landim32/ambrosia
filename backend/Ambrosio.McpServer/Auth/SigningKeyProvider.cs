using System.Security.Cryptography;
using Ambrosio.DTO.Settings;
using Microsoft.IdentityModel.Tokens;

namespace Ambrosio.McpServer.Auth;

/// <summary>Como o host conseguiu a chave de assinatura, para registrar em log.</summary>
public enum SigningKeySource
{
    /// <summary>Veio de <c>Auth__SigningKeyPem</c> — o caminho de Homolog e Production.</summary>
    Configured = 0,

    /// <summary>Chave local de Development, lida de ou gravada em <c>%APPDATA%/Ambrosio</c>.</summary>
    DevelopmentLocal = 1
}

/// <summary>
/// Resolve a chave RSA que assina os tokens do authorization server (feature 007, R13).
/// </summary>
/// <remarks>
/// A chave é o ponto único de falha de todas as conexões do ChatGPT: se ela mudar, todo access token
/// e refresh token emitidos antes deixa de validar no JWKS e cada conta precisa conectar de novo. Por
/// isso <see cref="AuthServerSettings.SigningKeyPem"/> é obrigatória em Homolog e Production (o
/// startup recusa — constituição §VI, emenda 3.4.0) e, em Development, a chave gerada localmente é
/// <b>persistida</b> em <c>%APPDATA%/Ambrosio/mcp-signing.pem</c> em vez de viver só em memória: um
/// <c>dotnet run</c> novo não derruba as conexões do dia anterior.
/// <para>
/// O PEM chega pela variável de ambiente em <b>linha única</b>, com <c>\n</c> literais no lugar das
/// quebras reais (o <c>.env</c> e os GitHub Secrets não comportam valor multilinha), então a
/// normalização acontece aqui, na fronteira da configuração.
/// </para>
/// <para>
/// O construtor recebe a settings já ligada e o ambiente, sem <c>ILogger</c>: quem consome esta
/// classe é o <c>AddOpenIddict()</c>, que roda durante o registro de serviços — antes de qualquer
/// container existir. Pedir logger aqui obrigaria um <c>BuildServiceProvider()</c> no meio do
/// registro, que é justamente o que produz dois singletones e duas chaves locais diferentes.
/// </para>
/// </remarks>
public sealed class SigningKeyProvider
{
    /// <summary>Nome do arquivo de chave local, dentro de <c>%APPDATA%/Ambrosio/</c>.</summary>
    public const string DevelopmentKeyFileName = "mcp-signing.pem";

    /// <summary>2048 é o piso do RFC 7518 para RS256 em uso novo.</summary>
    public const int KeySizeBits = 2048;

    private readonly AuthServerSettings _settings;
    private readonly IHostEnvironment _environment;
    private readonly Lazy<RsaSecurityKey> _key;
    private readonly Lazy<RsaSecurityKey> _validationKey;

    public SigningKeyProvider(AuthServerSettings settings, IHostEnvironment environment)
    {
        _settings = settings;
        _environment = environment;

        // Lazy com publicação de execução: resolve uma vez e usa a MESMA instância de RSA por toda a
        // vida do processo. Resolver por chamada custaria um parse de PEM por token emitido.
        _key = new Lazy<RsaSecurityKey>(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

        // Derivada da _key já resolvida: expor a pública é um ExportParameters(false), e refazer isso
        // a cada requisição validada seria trabalho criptográfico repetido por nada.
        _validationKey = new Lazy<RsaSecurityKey>(
            () => new RsaSecurityKey(_key.Value.Rsa!.ExportParameters(includePrivateParameters: false))
            {
                KeyId = _key.Value.KeyId
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Como a chave foi obtida. Só é confiável depois de tocar <see cref="SecurityKey"/>.</summary>
    public SigningKeySource Source { get; private set; }

    /// <summary>Caminho do arquivo local, quando a fonte for <see cref="SigningKeySource.DevelopmentLocal"/>.</summary>
    public string? LocalKeyPath { get; private set; }

    /// <summary>
    /// Chave assinadora com <c>kid</c> estável: vai ao <c>AddSigningKey</c> do OpenIddict e é o que o
    /// JWKS publica para o MCP validar.
    /// </summary>
    public RsaSecurityKey SecurityKey => _key.Value;

    /// <summary><c>kid</c> publicado no JWKS; idêntico em qualquer processo com a mesma chave.</summary>
    public string KeyId => SecurityKey.KeyId;

    /// <summary>
    /// Mesma chave, mas só com a parte <b>pública</b>: é o que o JwtBearer do MCP usa para validar
    /// assinatura. Confiar ao validador um <see cref="RsaSecurityKey"/> montado com a chave privada
    /// completa funcionaria criptograficamente, mas deixaria o segredo ao alcance de um stack trace
    /// ou de um <c>ToString()</c> — e validar nunca precisa dele.
    /// </summary>
    public RsaSecurityKey ValidationKey => _validationKey.Value;

    private RsaSecurityKey Resolve()
    {
        var pem = NormalizePem(_settings.SigningKeyPem);

        if (!string.IsNullOrWhiteSpace(pem))
        {
            var configured = RSA.Create();
            try
            {
                configured.ImportFromPem(pem!);
            }
            catch (FormatException)
            {
                configured.Dispose();
                // Nunca ecoa o conteúdo da chave: esta exceção chega ao log do container.
                throw new InvalidOperationException(
                    "Auth__SigningKeyPem não é um PEM PKCS#8 RSA válido. Gere com "
                    + "'openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048' e grave no "
                    + "ambiente em linha única, com \\n entre as linhas.");
            }

            Source = SigningKeySource.Configured;
            return Wrap(configured);
        }

        Source = SigningKeySource.DevelopmentLocal;
        return LoadOrCreateDevelopmentKey();
    }

    /// <summary>
    /// Troca <c>\n</c> e <c>\r\n</c> literais (dois caracteres cada) por quebras reais. Um PEM que já
    /// chega multilinha passa intacto.
    /// </summary>
    internal static string? NormalizePem(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        return pem
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private RsaSecurityKey LoadOrCreateDevelopmentKey()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ambrosio");
        var path = Path.Combine(directory, DevelopmentKeyFileName);
        LocalKeyPath = path;

        if (!_environment.IsDevelopment())
        {
            // Barreira explícita: um ambiente novo que chegasse aqui subiria assinando com chave
            // efêmera e derrubaria as conexões no primeiro restart — o pior modo de falha possível
            // para este recurso. O startup (T010) já recusa antes, mas a checagem fica.
            throw new InvalidOperationException(
                "Auth__SigningKeyPem é obrigatória fora de Development.");
        }

        if (File.Exists(path))
        {
            var saved = NormalizePem(File.ReadAllText(path));
            if (!string.IsNullOrWhiteSpace(saved))
            {
                var rsa = RSA.Create();
                try
                {
                    rsa.ImportFromPem(saved!);
                    return Wrap(rsa);
                }
                catch (FormatException)
                {
                    rsa.Dispose();
                    // PEM corrompido no arquivo local: regenerar é o único reparo possível, e o
                    // custo (perder conexões) é avisado pela origem informada em log no Program.
                }
            }
        }

        var generated = RSA.Create(KeySizeBits);
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, generated.ExportPkcs8PrivateKeyPem());

        return Wrap(generated);
    }

    private static RsaSecurityKey Wrap(RSA rsa) => new(rsa) { KeyId = ComputeKeyId(rsa) };

    /// <summary>
    /// <c>kid</c> derivado da chave <b>pública</b> (SHA-256 do SubjectPublicKeyInfo, 16 hex
    /// minúsculos). A privada nunca entra em identificador publicado no JWKS, e dois containers do
    /// mesmo host chegam ao mesmo <c>kid</c> sem trocar segredo.
    /// </summary>
    private static string ComputeKeyId(RSA rsa)
    {
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        return Convert.ToHexString(SHA256.HashData(publicKey))[..16].ToLowerInvariant();
    }
}
