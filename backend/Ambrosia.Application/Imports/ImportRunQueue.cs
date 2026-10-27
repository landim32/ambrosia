using System.Threading.Channels;
using Ambrosia.Domain.Common;
using Ambrosia.Domain.Interfaces;

namespace Ambrosia.Application.Imports;

/// <summary>
/// Fila em memória dos trabalhos de importação (feature 006, research §4): <c>Channel&lt;ImportJob&gt;</c>
/// ilimitado, singleton.
/// <para>
/// Fila externa (Hangfire, RabbitMQ, Quartz) foi a alternativa rejeitada porque persistiria os
/// argumentos do job — e os argumentos contêm a senha do +Vendas (FR-006). O preço está registrado em
/// Complexity Tracking: com fila em memória, a API roda em <b>uma</b> réplica nesta entrega.
/// </para>
/// <para>
/// Ilimitado de propósito: <c>POST /runs</c> precisa responder <c>202</c> em até 3 s (SC-006) sem
/// esperar vaga de execução — quem limita a concorrência é o semáforo do worker, e a
/// unicidade por vendedora é o índice parcial do banco (FR-016), não o tamanho da fila.
/// </para>
/// </summary>
public class ImportRunQueue : IImportRunQueue
{
    private readonly Channel<ImportJob> _channel;

    public ImportRunQueue()
    {
        _channel = Channel.CreateUnbounded<ImportJob>(new UnboundedChannelOptions
        {
            // Uma leitura só (o worker) e fila FIFO: a ordem de chegada é a ordem de execução.
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public void Enqueue(ImportJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        // Unbounded: Writer.TryWrite nunca bloqueia nem falha por capacidade.
        _channel.Writer.TryWrite(job);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ImportJob> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return job;
        }
    }
}
