using Ambrosia.API.Extensions;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Common;
using Ambrosia.DTO.Imports;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosia.API.Controllers;

/// <summary>
/// Importação dos pedidos do +Vendas (feature 006). Seis rotas, todas autenticadas: a vendedora informa
/// login e senha <b>a cada execução</b>, acompanha o progresso e consulta o histórico.
/// <para>
/// <b>Credenciais (FR-005/FR-006, SC-004)</b>: <c>POST /runs</c> é o único ponto do sistema que as
/// recebe, e nada aqui as devolve, grava ou loga. O corpo da requisição não é registrado nem em erro —
/// um <c>400</c> devolve apenas campo e mensagem, e as mensagens do validator não ecoam valor.
/// </para>
/// <para>
/// O <c>userId</c> vem exclusivamente do claim <c>sub</c> (constituição §VI); o DTO de entrada carrega
/// as credenciais <b>do +Vendas</b>, nunca um dono. Execução de outra vendedora responde 404 igual a
/// inexistente (FR-053).
/// </para>
/// </summary>
[Route("imports/mais-vendas")]
[Authorize]
public class ImportsController : ApiControllerBase
{
    private readonly IImportRunService _imports;

    public ImportsController(IImportRunService imports)
    {
        _imports = imports;
    }

    /// <summary>
    /// Dados que pré-preenchem a tela: data inicial sugerida (último pedido importado; <c>null</c>
    /// quando não há), data final = hoje em São Paulo, tamanho do bloco, limite do período e o horário
    /// de liberação quando há bloqueio por recusas (FR-002/FR-008a).
    /// </summary>
    /// <response code="200">Sugestões e limites vigentes.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("defaults")]
    [ProducesResponseType(typeof(ImportDefaultsInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDefaults()
    {
        try
        {
            return Ok(await _imports.GetDefaultsAsync(User.GetUserId()));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Inicia uma importação (FR-001/FR-016). Valida o período, grava a execução como
    /// <c>queued</c> <b>sem credencial no banco</b>, enfileira o trabalho e responde <c>202</c> com o
    /// resumo — a coleta roda no worker, e é por isso que a resposta é imediata (SC-006, ≤ 3 s).
    /// <para>
    /// O corpo é <see cref="ImportRunStartInfo"/>: <c>login</c> e <c>password</c> do +Vendas
    /// (documentados no Swagger como <c>format: password</c>), <c>startDate</c>/<c>endDate</c> e
    /// <c>dryRun</c>. Ele nunca é registrado no log.
    /// </para>
    /// </summary>
    /// <param name="info">Credenciais desta execução e período.</param>
    /// <response code="202">Execução criada e enfileirada (<c>status: queued</c>).</response>
    /// <response code="400">Período ou credenciais inválidos; as mensagens não repetem o valor enviado.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="409">Já existe importação em andamento.</response>
    /// <response code="429">Bloqueio por recusas seguidas de credencial; ver <c>Retry-After</c>.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("runs")]
    [ProducesResponseType(typeof(ImportRunInfo), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Start([FromBody] ImportRunStartInfo info)
    {
        try
        {
            if (ModelStateError() is { } badRequest)
            {
                return badRequest;
            }

            var created = await _imports.StartAsync(User.GetUserId(), info);

            return AcceptedAtAction(nameof(GetRun), new { runId = created.Id }, created);
        }
        catch (ValidationException ex)
        {
            // Sem log do corpo (FR-006): um LogError com `info` levaria a senha para o stderr.
            return ValidationError(ex);
        }
        catch (ImportLockedException ex)
        {
            return ImportLockedError(ex);
        }
        catch (DuplicateException ex)
        {
            return ConflictError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Execução ativa da vendedora (<c>queued</c> ou <c>running</c>) com o detalhe, para a tela retomar
    /// o progresso depois de fechar e reabrir a aba (FR-014/US7-1). <c>204</c> quando não há nenhuma.
    /// </summary>
    /// <response code="200">Detalhe da execução em curso.</response>
    /// <response code="204">Nenhuma execução ativa.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("runs/current")]
    [ProducesResponseType(typeof(ImportRunDetailInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetCurrent()
    {
        try
        {
            var current = await _imports.GetCurrentAsync(User.GetUserId());

            return current is null ? NoContent() : Ok(current);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Relatório de uma execução: blocos com as nove contagens, rastro pedido a pedido, agrupamento
    /// canal × tipo e a comparação com a execução anterior do mesmo período (FR-048/FR-049/FR-051).
    /// </summary>
    /// <param name="runId">Identificador da execução.</param>
    /// <response code="200">Detalhe da execução.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Execução inexistente ou de outra vendedora (indistinguíveis).</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("runs/{runId:long}")]
    [ProducesResponseType(typeof(ImportRunDetailInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRun(long runId)
    {
        try
        {
            var detail = await _imports.GetByIdAsync(User.GetUserId(), runId);

            return detail is null ? NotFoundError(new NotFoundException(RUN_NOT_FOUND)) : Ok(detail);
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Histórico paginado das execuções da vendedora, por <c>requestedAt</c> decrescente (FR-045).
    /// </summary>
    /// <param name="query">Paginação (<c>page</c> ≥ 1, <c>pageSize</c> 1-50).</param>
    /// <response code="200">Página de execuções.</response>
    /// <response code="400">Paginação inválida.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("runs")]
    [ProducesResponseType(typeof(PagedResult<ImportRunInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] ImportRunListQuery query)
    {
        try
        {
            return Ok(await _imports.ListAsync(User.GetUserId(), query));
        }
        catch (ValidationException ex)
        {
            return ValidationError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Pede o cancelamento de uma execução (FR-019). Em <c>queued</c>, encerra de imediato; em
    /// <c>running</c>, o bloco em curso termina inteiro — é o que permite retomar sem perder o que já
    /// entrou — e o status vira <c>cancelled</c>.
    /// </summary>
    /// <param name="runId">Identificador da execução.</param>
    /// <response code="202">Cancelamento aceito (<c>cancelRequested: true</c> no detalhe).</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="404">Execução inexistente ou de outra vendedora.</response>
    /// <response code="409">A execução já foi finalizada.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("runs/{runId:long}/cancel")]
    [ProducesResponseType(typeof(ImportRunDetailInfo), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long runId)
    {
        try
        {
            var detail = await _imports.CancelAsync(User.GetUserId(), runId);

            return Accepted(detail);
        }
        catch (NotFoundException ex)
        {
            return NotFoundError(ex);
        }
        catch (ImportRunNotCancellableException ex)
        {
            return NotCancellableError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>Mensagem única do 404 de execução — não distingue inexistente de outra vendedora (FR-053).</summary>
    private const string RUN_NOT_FOUND = "Execução de importação não encontrada.";
}
