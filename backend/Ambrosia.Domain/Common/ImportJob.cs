using Ambrosia.DTO.Enums;

namespace Ambrosia.Domain.Common;

/// <summary>
/// O trabalho enfileirado para uma execução de importação (feature 006, research §4): é o único objeto
/// do sistema que carrega login e senha do +Vendas, e ele nunca sai da memória do processo.
/// <para>
/// <b>Nunca</b> é registrado em DI, serializado para resposta, escrito em banco, usado como estado de
/// log ou incluído em mensagem de exceção (FR-005/FR-006, SC-004). O <c>ImportRunWorker</c> o recebe
/// da fila, entrega ao <c>IImportExecutionService</c> e chama <see cref="Clear"/> no <c>finally</c>,
/// com sucesso, falha ou cancelamento — as referências somem e o coletor de lixo tem como reclaimá-las.
/// </para>
/// <para>
/// Limite honesto registrado no research §4: strings são imutáveis no .NET e a limpeza determinística
/// da memória gerenciada não é garantida; aceita-se porque o processo que as contém é o mesmo que
/// recebeu a requisição HTTPS, sem espelho em disco, log ou banco.
/// </para>
/// </summary>
public sealed class ImportJob
{
    public required long RunId { get; init; }

    public required long UserId { get; init; }

    public required ImportRunMode Mode { get; init; }

    public required DateOnly StartDate { get; init; }

    public required DateOnly EndDate { get; init; }

    /// <summary>Login do +Vendas. Só em memória (com <c>set</c> justamente para o <see cref="Clear"/> poder zerá-lo).</summary>
    public required string Login { get; set; }

    /// <summary>Senha do +Vendas. Só em memória, e este é o motivo de <see cref="Clear"/> existir.</summary>
    public required string Password { get; set; }

    /// <summary>
    /// Descarta as credenciais do objeto (FR-005). Chamado no <c>finally</c> do worker. Depois disto o
    /// job é inutilizável — a execução já terminou, e reler a senha não tem caso de uso legítimo.
    /// </summary>
    public void Clear()
    {
        Login = string.Empty;
        Password = string.Empty;
    }

    /// <summary>
    /// Nunca contém credencial alguma: um <c>ToString()</c> incidental em log, interpolação ou dump de
    /// depuração não pode vazar a senha (research §14).
    /// </summary>
    public override string ToString() =>
        $"ImportJob(RunId={RunId}, UserId={UserId}, Mode={Mode}, {StartDate:yyyy-MM-dd}–{EndDate:yyyy-MM-dd})";
}
