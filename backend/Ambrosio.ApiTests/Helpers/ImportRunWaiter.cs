using System.Diagnostics;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using FluentAssertions;

namespace Ambrosio.ApiTests.Helpers;

/// <summary>
/// Espera uma execução de importação terminar (feature 006, skill <c>dotnet-test-api</c>, research §16).
/// <para>
/// O contrato é assimétrico de propósito: <c>POST /runs</c> responde <c>202</c> com a execução
/// <c>queued</c> e o trabalho segue no worker, então nenhum teste de API pode afirmar o resultado sem
/// antes acompanhar o progresso. Este helper faz polling de <c>GET /imports/mais-vendas/runs/{id}</c>
/// até um status final — o mesmo caminho que a tela usa, só que mais rápido que os 3 s de
/// <c>IMPORT_POLL_MS</c>.
/// </para>
/// <para>
/// O teto é <see cref="ApiTestFixture.ImportRunTimeoutSeconds"/> (180 s no appsettings.Test.json).
/// Estourar o teto é falha de teste com estado: o relatório do último ciclo é impresso junto, porque
/// "a execução nunca terminou" sem os blocos não diz nada de útil.
/// </para>
/// </summary>
internal static class ImportRunWaiter
{
    /// <summary>Intervalo entre leituras do relatório (curto: o fake é rápido e o teste espera segundos).</summary>
    private const int POLL_INTERVAL_MS = 400;

    /// <summary>
    /// Polling até a execução sair de <c>queued</c>/<c>running</c>. Devolve o detalhe final.
    /// </summary>
    /// <param name="fixture">Fixture com o token da vendedora que iniciou a execução.</param>
    /// <param name="runId">Identificador devolvido pelo <c>202</c>.</param>
    /// <param name="token">Token da dona da execução (o helper não inventa identidade — §VI).</param>
    public static async Task<ImportRunDetailInfo> WaitUntilFinishedAsync(
        this ApiTestFixture fixture,
        long runId,
        string token)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * fixture.ImportRunTimeoutSeconds;
        HttpCallResult? last = null;

        while (Stopwatch.GetTimestamp() < deadline)
        {
            last = await fixture.GetAsync($"/imports/mais-vendas/runs/{runId}", token);

            // 404 aqui é pane do teste, não do servidor: a execução foi criada com o mesmo token, e o
            // histórico não some. Dar a mensagem crua é mais útil que esperar o timeout.
            last.StatusCode.Should().Be(200,
                $"a execução {runId} precisa ser legível pela própria dona durante o acompanhamento");

            var detail = last.As<ImportRunDetailInfo>();

            if (IsFinal(detail.Status))
            {
                return detail;
            }

            await Task.Delay(POLL_INTERVAL_MS);
        }

        var state = last is null ? "(nenhuma leitura)" : last.Body;

        throw new InvalidOperationException(
            $"A execução {runId} não terminou em {fixture.ImportRunTimeoutSeconds}s. Último estado: {state}");
    }

    /// <summary>
    /// Polling até os blocos progredirem o suficiente para a tela mostrar progresso — usada pelo teste
    /// que confere o <c>running</c> intermediário em vez do resultado final.
    /// </summary>
    /// <param name="minBlocksFinished">Quantos blocos precisam estar terminados.</param>
    public static async Task<ImportRunDetailInfo> WaitUntilBlocksProgressAsync(
        this ApiTestFixture fixture,
        long runId,
        string token,
        int minBlocksFinished)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * fixture.ImportRunTimeoutSeconds;

        while (Stopwatch.GetTimestamp() < deadline)
        {
            var response = await fixture.GetAsync($"/imports/mais-vendas/runs/{runId}", token);
            var detail = response.As<ImportRunDetailInfo>();

            if (detail.BlocksFinished >= minBlocksFinished || IsFinal(detail.Status))
            {
                return detail;
            }

            await Task.Delay(POLL_INTERVAL_MS);
        }

        throw new InvalidOperationException(
            $"A execução {runId} não chegou a {minBlocksFinished} bloco(s) concluído(s) " +
            $"em {fixture.ImportRunTimeoutSeconds}s.");
    }

    /// <summary>Os quatro status finais do contrato (research §18).</summary>
    public static bool IsFinal(ImportRunStatus status) =>
        status is ImportRunStatus.Succeeded
            or ImportRunStatus.Failed
            or ImportRunStatus.Partial
            or ImportRunStatus.Cancelled;

    /// <summary>Espera o cancelamento de uma execução <c>queued</c> virar status final.</summary>
    public static async Task<ImportRunDetailInfo> WaitUntilStatusAsync(
        this ApiTestFixture fixture,
        long runId,
        string token,
        ImportRunStatus expected)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * fixture.ImportRunTimeoutSeconds;
        HttpCallResult? last = null;

        while (Stopwatch.GetTimestamp() < deadline)
        {
            last = await fixture.GetAsync($"/imports/mais-vendas/runs/{runId}", token);

            var detail = last.As<ImportRunDetailInfo>();

            // Um status final diferente do esperado já é resposta: esperar só trocaria uma falha clara
            // por um timeout sem informação.
            if (detail.Status == expected)
            {
                return detail;
            }

            if (IsFinal(detail.Status) && detail.Status != expected)
            {
                detail.Status.Should().Be(expected,
                    $"a execução fechou antes (blocos: {DescribeBlocks(detail)})");
            }

            await Task.Delay(POLL_INTERVAL_MS);
        }

        throw new InvalidOperationException(
            $"A execução {runId} não chegou a {expected} em {fixture.ImportRunTimeoutSeconds}s. " +
            $"Último estado: {last?.Body}");
    }

    private static string DescribeBlocks(ImportRunDetailInfo detail) =>
        string.Join(", ", detail.Blocks.Select(b => $"{b.BlockNo}:{b.Status}"));
}
