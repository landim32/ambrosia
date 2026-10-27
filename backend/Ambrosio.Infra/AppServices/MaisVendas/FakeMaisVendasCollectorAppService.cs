using System.Reflection;
using Ambrosio.Domain.Common;
using Ambrosio.Domain.Exceptions;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports.Collector;
using Ambrosio.DTO.Settings;
using Ambrosio.Infra.Interfaces.AppServices;
using Microsoft.Extensions.Options;

namespace Ambrosio.Infra.AppServices.MaisVendas;

/// <summary>
/// Origem de teste controlada da importação (feature 006, research §3). Substitui o +Vendas na
/// <b>homologação</b> para que a constituição §VIII possa ser cumprida: o backend só é testado por
/// testes de API, e o +Vendas real não pode ser manipulado para provocar falha no 15º pedido,
/// divergência de contadores, foto com erro ou conta diferente.
/// <para>
/// Determinístico e sem rede, guiado pelo próprio login e pela senha: <c>conta+cenario</c> escolhe o
/// cenário, e a senha é a versão do conteúdo. A parte antes do <c>+</c> é o identificador da conta
/// (vira o hash do vínculo — FR-008b). O cenário <c>ok</c> devolve os 49 pedidos entre 26/09 e
/// 25/10/2026 somando o líquido de R$ 123.300,58 (SC-001).
/// </para>
/// <para>
/// Recusado em Production por <c>Program.ValidateSettings</c> (research §13): um fake "importando com
/// sucesso" corromperia a base da vendedora.
/// </para>
/// </summary>
public class FakeMaisVendasCollectorAppService : IMaisVendasCollectorAppService
{
    /// <summary>Versão registrada em <c>import_runs.collector_version</c> (data-model §2).</summary>
    public const string VERSION = "fake-1.0";

    private readonly MaisVendasSettings _settings;

    public FakeMaisVendasCollectorAppService(IOptions<MaisVendasSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <inheritdoc />
    public string CollectorVersion => VERSION;

    /// <inheritdoc />
    public Task<IMaisVendasCollectorSession> OpenSessionAsync(
        MaisVendasCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        cancellationToken.ThrowIfCancellationRequested();

        var (account, scenario) = ParseLogin(credentials.Login);
        var refused = IsRefusedPassword(credentials.Password);
        var version = ParseVersion(credentials.Password);

        return Task.FromResult<IMaisVendasCollectorSession>(
            new FakeSession(account, scenario, version, refused, _settings.Fake.DelayMs));
    }

    /// <summary>
    /// Login <c>conta+cenario</c>: antes do <c>+</c> é o identificador da conta (FR-008b), depois é o
    /// cenário. Sem <c>+</c> o cenário é <c>ok</c> — é o caminho do quickstart quando a vendedora só
    /// digita <c>s482</c>.
    /// </summary>
    internal static (string Account, string Scenario) ParseLogin(string? login)
    {
        var value = (login ?? string.Empty).Trim();

        var plus = value.IndexOf('+', StringComparison.Ordinal);

        if (plus < 0)
        {
            return (value, Scenario.Ok);
        }

        var account = value[..plus];
        var scenario = value[(plus + 1)..].Trim().ToLowerInvariant();

        return (account, scenario.Length == 0 ? Scenario.Ok : scenario);
    }

    /// <summary>Senha que simula credencial recusada pela origem (FR-007/FR-008a).</summary>
    internal static bool IsRefusedPassword(string? password) =>
        string.Equals((password ?? string.Empty).Trim(), Scenario.REFUSED_PASSWORD, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Senha = <b>versão do conteúdo</b> (research §3): <c>v1</c>, <c>v2</c>, <c>v3</c>. Tudo que não é
    /// versão reconhecível é a base <c>v1</c> — inclusive uma senha qualquer digitada na tela de
    /// homologação.
    /// </summary>
    internal static int ParseVersion(string? password)
    {
        var value = (password ?? string.Empty).Trim().ToLowerInvariant();

        return value.StartsWith('v') && int.TryParse(value.AsSpan(1), out var version) && version > 0
            ? version
            : 1;
    }

    /// <summary>Nomes dos cenários (research §3), cada um exercitado pela suíte de API.</summary>
    internal static class Scenario
    {
        public const string Ok = "ok";
        public const string Empty = "vazio";
        public const string FailBlock3 = "falha-bloco3";
        public const string CounterMismatch = "contador-divergente";
        public const string TotalMismatch = "total-divergente";
        public const string PhotoError = "foto-erro";
        public const string PhotoGif = "foto-gif";
        public const string InvalidCpf = "cpf-invalido";
        public const string YearlessDates = "sem-ano";
        public const string TwoFactor = "2fa";
        public const string Unstable = "instavel";

        /// <summary>Senha que produz <see cref="MaisVendasLoginOutcome.Refused"/>.</summary>
        public const string REFUSED_PASSWORD = "recusada";
    }

    /// <summary>
    /// A sessão do fake: cenário, versão do conteúdo, identificador da conta e o atraso entre pedidos
    /// (para o progresso ser observável na tela e nos testes sem depender de rede).
    /// </summary>
    private sealed class FakeSession : IMaisVendasCollectorSession
    {
        /// <summary>Chamadas de coleta feitas, para o cenário <c>instavel</c> falhar só na primeira.</summary>
        private int _collectCalls;

        private bool _disposed;

        public FakeSession(string account, string scenario, int version, bool refused, int delayMs)
        {
            Account = account;
            ScenarioName = scenario;
            Version = version;
            Refused = refused;
            DelayMs = delayMs;
        }

        private string Account { get; }

        private string ScenarioName { get; }

        private int Version { get; }

        private bool Refused { get; }

        private int DelayMs { get; }

        /// <inheritdoc />
        public async Task<MaisVendasLoginResult> LoginAsync(CancellationToken cancellationToken = default)
        {
            await EnsureOpenAsync(cancellationToken);
            await DelayAsync(cancellationToken);

            if (ScenarioName == Scenario.TwoFactor)
            {
                // A origem pediu código/captcha: o coletor não tenta adivinhar (FR-046).
                return MaisVendasLoginResult.Unsupported();
            }

            return Refused
                ? MaisVendasLoginResult.Refused()
                : MaisVendasLoginResult.Accepted(Account);
        }

        /// <inheritdoc />
        public async Task<CollectedBlock> CollectBlockAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default)
        {
            await EnsureOpenAsync(cancellationToken);

            if (ScenarioName == Scenario.Unstable && Interlocked.Increment(ref _collectCalls) == 1)
            {
                // Falha transitória que as retentativas do serviço superam (FR-045, US3 "instavel").
                throw new MaisVendasSourceException("timeout transitório simulado na primeira tentativa");
            }

            var seeds = SeedsFor();
            var orders = new List<CollectedOrder>();

            for (var position = 0; position < seeds.Count; position++)
            {
                var seed = seeds[position];
                var date = MaisVendasFake.ParseDate(seed.Date);

                if (date < from || date > to)
                {
                    continue;
                }

                orders.Add(BuildOrder(seed, position, date, from, to));

                await DelayAsync(cancellationToken);
            }

            var counters = MaisVendasFake.DeriveCounters(orders);

            if (ScenarioName == Scenario.CounterMismatch)
            {
                // A origem anuncia um pedido a mais do que a lista entrega: o bloco tem de falhar
                // antes de gravar qualquer linha (FR-010, SC-009).
                counters.Counters[new CollectorTabKey(OrderOrigin.Cota, CollectorTab.All)]++;
            }
            else if (ScenarioName == Scenario.TotalMismatch)
            {
                // Total anunciado fora da soma das linhas (FR-011).
                counters.Totals[new CollectorTabKey(OrderOrigin.Omni, CollectorTab.All)] += 999.99m;
            }

            return new CollectedBlock
            {
                Orders = orders,
                TabCounters = counters.Counters,
                TabTotals = counters.Totals
            };
        }

        /// <inheritdoc />
        public async Task<CollectedImage> DownloadImageAsync(
            string url,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            await EnsureOpenAsync(cancellationToken);
            await DelayAsync(cancellationToken);

            if (ScenarioName == Scenario.PhotoError && string.Equals(url, MaisVendasFake.ImageUrls[0], StringComparison.Ordinal))
            {
                // URL que a origem não serve: o item fica sem imagem e o pedido segue (FR-039, US6-2).
                throw new MaisVendasSourceException("a foto indicada não está disponível na origem");
            }

            var sku = SkuIndexOfUrl(url);

            if (ScenarioName == Scenario.PhotoGif && sku == 0)
            {
                // image/gif não está entre os tipos aceitos: vira "imagem com erro" (FR-039, US6-3).
                return await ReadEmbeddedAsync("tiny.gif", "image/gif", maxBytes, cancellationToken);
            }

            return await ReadEmbeddedAsync(
                MaisVendasFake.ImageResourceName(sku),
                MaisVendasFake.ImageMimeType(sku),
                maxBytes,
                cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _disposed = true;

            return ValueTask.CompletedTask;
        }

        /// <summary>Índice do SKU correspondente à URL pedida (URL desconhecida cai no 0).</summary>
        private static int SkuIndexOfUrl(string url)
        {
            for (var i = 0; i < MaisVendasFake.ImageUrls.Length; i++)
            {
                if (string.Equals(MaisVendasFake.ImageUrls[i], url, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>Monta o pedido, aplicando as mutações de cenário (CPF inválido, data sem ano).</summary>
        private CollectedOrder BuildOrder(MaisVendasFake.Seed seed, int position, DateOnly date, DateOnly from, DateOnly to)
        {
            var order = MaisVendasFake.ToCollected(seed, Version, CpfFor(seed, position));

            if (ScenarioName != Scenario.YearlessDates)
            {
                return order;
            }

            // A origem mostra "24 Out" na lista e o ano só aparece na URL de detalhe (research §10):
            // o coletor normaliza pelos dois caminhos — é exatamente o que o cenário exerce.
            var dayMonth = $"{date.Day} {MaisVendasNormalizer.MonthAbbreviation(date.Month)}";

            return order with
            {
                OrderDate = MaisVendasNormalizer.ResolveDate(dayMonth, from, to, date.Year)
            };
        }

        /// <summary>
        /// O conjunto de linhas do cenário corrente, já com a mutação aplicada sobre a versão pedida.
        /// </summary>
        private List<MaisVendasFake.Seed> SeedsFor()
        {
            if (ScenarioName == Scenario.Empty)
            {
                return [];
            }

            var seeds = new List<MaisVendasFake.Seed>(Version switch
            {
                >= 3 => MaisVendasFake.OkV3(),
                2 => MaisVendasFake.OkV2(),
                _ => MaisVendasFake.OkV1
            });

            if (ScenarioName == Scenario.FailBlock3)
            {
                // 15º registro do 3º bloco com número acima dos 60 caracteres da coluna: a gravação
                // estoura, o bloco falha (T076) e os 14 anteriores ficam gravados. A foto deste pedido
                // foi baixada antes dela falhar — é o caso da FR-041.
                var index = seeds.FindIndex(s => s.Number == MaisVendasFake.FAILING_ORDER_NUMBER);

                if (index >= 0)
                {
                    seeds[index] = seeds[index] with { Number = new string('9', 61) };
                }
            }

            return seeds;
        }

        /// <summary>
        /// CPF entregue para uma linha. O cenário <c>cpf-invalido</c> (T084) derruba dois: o do primeiro
        /// pedido do período (ausente) e o de <c>41900007</c> (dígito verificador errado). Os demais
        /// seguem válidos e a execução termina <c>partial</c> (FR-036).
        /// </summary>
        private string? CpfFor(MaisVendasFake.Seed seed, int position)
        {
            var digits = MaisVendasFake.CpfDigits[seed.Customer % MaisVendasFake.CpfDigits.Length];

            if (ScenarioName != Scenario.InvalidCpf)
            {
                return digits;
            }

            return position switch
            {
                0 => null,
                6 => "11144477736",
                _ => digits
            };
        }

        private Task EnsureOpenAsync(CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FakeSession), "A sessão de coleta já foi encerrada.");
            }

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;
        }

        private Task DelayAsync(CancellationToken cancellationToken) =>
            DelayMs > 0 ? Task.Delay(DelayMs, cancellationToken) : Task.CompletedTask;

        /// <summary>
        /// Lê o recurso embutido (T088) limitado a <paramref name="maxBytes"/> — o mesmo contrato de
        /// tamanho do coletor real (research §12).
        /// </summary>
        private static async Task<CollectedImage> ReadEmbeddedAsync(
            string fileName,
            string mimeType,
            long maxBytes,
            CancellationToken cancellationToken)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

            if (resource is null)
            {
                throw new MaisVendasSourceException($"o recurso de foto '{fileName}' não está no assembly");
            }

            await using var source = assembly.GetManifestResourceStream(resource)!;

            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            var total = 0L;

            while (true)
            {
                var read = await source.ReadAsync(chunk, cancellationToken);

                if (read == 0)
                {
                    break;
                }

                total += read;

                if (total > maxBytes)
                {
                    await buffer.DisposeAsync();

                    throw new MaisVendasSourceException("a foto excede o tamanho máximo aceito pelo storage");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            buffer.Position = 0;

            return new CollectedImage
            {
                Content = buffer,
                MimeType = mimeType,
                SizeBytes = total
            };
        }
    }
}
