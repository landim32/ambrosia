using Ambrosio.Domain.Models;
using Ambrosio.DTO.Imports;

namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Casos de uso da tela de importação (feature 006): defaults, iniciar, consultar, listar e cancelar.
/// <para>
/// Todo método recebe <c>userId</c> extraído do claim <c>sub</c> pelo controller — nenhuma entrada
/// transporta o dono, e execução de outra vendedora é <c>NotFoundException</c> (404), nunca uma
/// resposta que revele que existe (constituição §VI, FR-053).
/// </para>
/// </summary>
public interface IImportRunService
{
    /// <summary>
    /// Data inicial sugerida (último pedido importado, ou <c>null</c>), hoje em
    /// <c>America/Sao_Paulo</c>, tamanho do bloco, limite do período e o bloqueio vigente
    /// (FR-002/FR-008a).
    /// </summary>
    Task<ImportDefaultsInfo> GetDefaultsAsync(long userId);

    /// <summary>
    /// Valida, cria a execução <c>queued</c> <b>sem credencial no banco</b>, enfileira o job e devolve
    /// o resumo (FR-001/FR-016).
    /// </summary>
    /// <exception cref="FluentValidation.ValidationException">Período ou credenciais inválidos (400).</exception>
    /// <exception cref="Ambrosio.Domain.Exceptions.ImportLockedException">Bloqueio por recusas (429).</exception>
    /// <exception cref="Ambrosio.Domain.Exceptions.DuplicateException">Já há execução ativa (409).</exception>
    Task<ImportRunInfo> StartAsync(long userId, ImportRunStartInfo info);

    /// <summary>Execução ativa (<c>queued</c>/<c>running</c>) com o detalhe, ou <c>null</c> (204).</summary>
    Task<ImportRunDetailInfo?> GetCurrentAsync(long userId);

    /// <summary>Relatório de uma execução; <c>null</c> quando não existe ou é de outra vendedora (404).</summary>
    Task<ImportRunDetailInfo?> GetByIdAsync(long userId, long runId);

    /// <summary>Histórico paginado, por <c>requestedAt</c> decrescente (FR-045).</summary>
    Task<Ambrosio.DTO.Common.PagedResult<ImportRunInfo>> ListAsync(long userId, ImportRunListQuery query);

    /// <summary>
    /// Pede o cancelamento (FR-019): em <c>queued</c> vira <c>cancelled</c> de imediato; em
    /// <c>running</c> marca <c>cancel_requested</c> e sinaliza o worker, encerrando ao fim do bloco
    /// atual.
    /// </summary>
    /// <exception cref="Ambrosio.Domain.Exceptions.NotFoundException">Não existe ou é de outra vendedora (404).</exception>
    /// <exception cref="Ambrosio.Domain.Exceptions.ImportRunNotCancellableException">Já finalizada (409).</exception>
    Task<ImportRunDetailInfo> CancelAsync(long userId, long runId);
}
