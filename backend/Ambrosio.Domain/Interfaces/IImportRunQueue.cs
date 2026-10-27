using Ambrosio.Domain.Common;

namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Fila em memória dos trabalhos de importação (feature 006, research §4): o <c>POST /runs</c>
/// enfileira e o <c>ImportRunWorker</c> consome.
/// <para>
/// Implementada sobre <c>Channel&lt;ImportJob&gt;</c> ilimitado (Application) porque uma fila externa
/// — Hangfire, RabbitMQ — persistiria os argumentos do job, e os argumentos contêm a senha (a
/// alternativa rejeitada no research §4). Consequência registrada em Complexity Tracking: fila em
/// memória significa <b>uma</b> réplica da API nesta entrega.
/// </para>
/// <para>
/// Como <see cref="ImportJob"/> carrega credencial, esta interface é o único caminho pelo qual o job
/// circula no processo: nada aqui é registrado como serviço por requisição e nada é logado.
/// </para>
/// </summary>
public interface IImportRunQueue
{
    /// <summary>Enfileira o trabalho (não bloqueia, fila ilimitada).</summary>
    void Enqueue(ImportJob job);

    /// <summary>
    /// Lê a fila continuamente (consumo único do worker). Termina com
    /// <see cref="OperationCanceledException"/> no shutdown do host.
    /// </summary>
    IAsyncEnumerable<ImportJob> ReadAllAsync(CancellationToken cancellationToken = default);
}
