using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Common;

/// <summary>
/// Trabalho de importação enfileirado (feature 006, research §4; reescrito pela feature 008,
/// research R7). Carrega só o que é preciso para <b>identificar</b> a execução: chave, dono, modo e
/// período.
/// <para>
/// Até a feature 006 este objeto transportava <c>Login</c> e <c>Password</c> em claro da requisição até
/// o navegador, e foi por isso que a fila foi feita em memória (persistir o job seria persistir a
/// senha). Com as credenciais cifradas no banco (FR-003 da 008), o job não tem mais segredo nenhum: o
/// executor os decifra no instante do login na origem, por <c>UserId</c>
/// (<see cref="Interfaces.IMaisVendasCredentialService.GetDecryptedAsync"/>).
/// </para>
/// <para>
/// Continuam aqui as datas e o modo porque a execução é planejada no <c>Create</c>, antes de o worker
/// pegar o trabalho. <see cref="Clear"/> mantém o contrato de limpeza: zerar o estado de uma fila que
/// hoje não tem credencial custa zero e protege contra alguém reintroduzir uma no futuro.
/// </para>
/// </summary>
public sealed class ImportJob
{
    // As cinco propriedades são `set` (não `init`) de propósito: `Clear` precisa zerar o conteúdo no
    // finally do worker. Com `init` o compilador proíbe, e o contrato de limpeza viraria comentário.
    /// <summary>Identificador da execução já persistida (o <c>import_run_id</c> da fila).</summary>
    public long RunId { get; set; }

    /// <summary>Dono, do claim <c>sub</c>. É a chave com que o repositório lê a execução (FR-053).</summary>
    public long UserId { get; set; }

    /// <summary>Simulação ou gravação — decide se <c>Apply</c> escreve em algum lugar (FR-032).</summary>
    public ImportRunMode Mode { get; set; }

    /// <summary>Início do período, inclusive.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Fim do período, inclusive.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Zera o conteúdo. Chamado no <c>finally</c> do worker com sucesso, falha ou cancelamento.
    /// </summary>
    public void Clear()
    {
        RunId = 0;
        UserId = 0;
        StartDate = default;
        EndDate = default;
    }

    /// <summary>Nunca mostra o conteúdo (padrão herdado da versão que carregava credencial).</summary>
    public override string ToString() => $"ImportJob(RunId={RunId})";
}
