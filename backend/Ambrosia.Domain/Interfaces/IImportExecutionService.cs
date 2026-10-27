using Ambrosia.Domain.Common;

namespace Ambrosia.Domain.Interfaces;

/// <summary>
/// Orquestração de uma execução de importação (feature 006, research §1/§5): login na origem, vínculo
/// e bloqueio da conta, coleta bloco a bloco, validação dos contadores <b>antes</b> de gravar, gravação
/// atômica por pedido, clientes sob demanda, fotos, não encontrados e fechamento com a FR-047a.
/// <para>
/// Chamada <b>somente</b> pelo <c>ImportRunWorker</c> em escopo de DI próprio — nunca por um endpoint:
/// é o que mantém a credencial fora do ciclo de uma requisição (FR-005).
/// </para>
/// </summary>
public interface IImportExecutionService
{
    /// <summary>
    /// Roda a execução inteira, tratando o cancelamento e as falhas de bloco, e escreve o estado final
    /// em <c>import_runs</c>. Não lança em erro de origem: a falha vira status <c>failed</c> com
    /// mensagem segura (FR-013/FR-045), porque o chamador é um serviço de fundo.
    /// </summary>
    Task ExecuteAsync(ImportJob job, CancellationToken cancellationToken = default);
}
