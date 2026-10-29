using Ambrosio.API.Extensions;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ambrosio.API.Controllers;

/// <summary>
/// Importação dos pedidos do +Vendas (feature 006) e as credenciais guardadas da vendedora
/// (feature 008). Rotas autenticadas: a vendedora cadastra login e senha <b>uma vez</b> na tela de
/// configuração e passa a importar sem redigitá-los.
/// <para>
/// <b>Credenciais (FR-003/SC-003)</b>: <c>PUT /credentials</c> é o único ponto do sistema que recebe a
/// senha do +Vendas em claro, e <c>POST /runs</c> não recebe mais nada — não tem campo de credencial no
/// corpo. Nenhuma rota devolve a senha, nem mascarada; o <c>GET</c> devolve o login (a vendedora
/// precisa dele para editar o próprio cadastro) e <c>hasPassword</c>. O corpo da requisição não é
/// registrado nem em erro — um <c>400</c> devolve apenas campo e mensagem, e as mensagens do validator
/// não ecoam valor.
/// </para>
/// <para>
/// O <c>userId</c> vem exclusivamente do claim <c>sub</c> (constituição §VI); nenhum DTO de entrada
/// transporta um dono. Execução de outra vendedora responde 404 igual a inexistente (FR-053 da 006), e
/// as credenciais de outra vendedora não existem para ninguém: a rota só conhece o <c>sub</c>.
/// </para>
/// </summary>
[Route("imports/mais-vendas")]
[Authorize]
public class ImportsController : ApiControllerBase
{
    private readonly IImportRunService _imports;
    private readonly IMaisVendasCredentialService _credentials;

    public ImportsController(IImportRunService imports, IMaisVendasCredentialService credentials)
    {
        _imports = imports;
        _credentials = credentials;
    }

    /// <summary>
    /// Credenciais do +Vendas da vendedora (feature 008, FR-001): login guardado em claro,
    /// <c>hasPassword</c>, datas de cadastro/troca e a situação (<c>none</c> | <c>valid</c> |
    /// <c>needsUpdate</c>). <b>A senha não sai daqui</b>, nem mascarada — é a leitura que a tela de
    /// configuração faz para poder editar o cadastro sem perguntar a senha de novo.
    /// </summary>
    /// <response code="200">Estado das credenciais (sem senha).</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpGet("credentials")]
    [ProducesResponseType(typeof(MaisVendasCredentialsInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCredentials()
    {
        try
        {
            return Ok(await _credentials.GetAsync(User.GetUserId()));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Cadastra ou troca as credenciais (feature 008, FR-002): o login e a senha são cifrados nesta
    /// transação e o valor em claro morre aqui — nada disso é gravado, logado ou devolvido.
    /// <para>
    /// <c>null</c> num campo significa "não mexi nele": com cadastro feito, manda-se só a senha para
    /// trocá-la; sem cadastro, login e senha vêm juntos (a recusa diz isso no campo que faltou).
    /// </para>
    /// </summary>
    /// <param name="info">Login e/ou senha; ambos opcionais no binding.</param>
    /// <response code="200">Estado novo (sem senha), <c>status: valid</c>.</response>
    /// <response code="400">Combinação ou formato recusado; as mensagens não repetem o valor enviado.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPut("credentials")]
    [ProducesResponseType(typeof(MaisVendasCredentialsInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveCredentials([FromBody] MaisVendasCredentialsUpdateInfo info)
    {
        try
        {
            return Ok(await _credentials.SaveAsync(User.GetUserId(), info));
        }
        catch (ValidationException ex)
        {
            // Sem log do corpo (FR-003): um LogError com `info` levaria a senha para o stderr.
            return ValidationError(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Remove as credenciais guardadas (feature 008, FR-006): zera as cinco colunas do cadastro.
    /// <c>account_hash</c> e o bloqueio por recusas não mudam — quem já foi bloqueado por recusas
    /// continua bloqueado, agora sem credenciais salvas. Idempotente: remover sem cadastro responde
    /// <c>200</c> com <c>hasPassword: false</c>.
    /// </summary>
    /// <response code="200">Estado vazio (<c>status: none</c>).</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpDelete("credentials")]
    [ProducesResponseType(typeof(MaisVendasCredentialsInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveCredentials()
    {
        try
        {
            return Ok(await _credentials.DeleteAsync(User.GetUserId()));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
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
    /// Inicia uma importação no período escolhido (feature 006, FR-001/FR-016; <b>feature 008</b>,
    /// FR-001/FR-002/FR-009). Valida o período, exige credenciais cadastradas, grava a execução como
    /// <c>queued</c> e responde <c>202</c> com o resumo — a coleta roda no worker da API, que pega a
    /// linha na fila do banco.
    /// <para>
    /// O corpo é <see cref="ImportRunStartInfo"/>: <c>startDate</c>, <c>endDate</c> e <c>dryRun</c>.
    /// <b>Não tem campo de credencial</b>: o que uma cliente antiga ainda enviar por login e senha é
    /// descartado no binding, e a execução não acontece sem cadastro na tela de configuração (a recusa
    /// vem no campo <c>login</c>, com a mesma frase do plugin).
    /// </para>
    /// </summary>
    /// <param name="info">Período e modo.</param>
    /// <response code="202">Execução criada e na fila (<c>status: queued</c>, <c>startedVia: screen</c>).</response>
    /// <response code="400">Período inválido ou credenciais não cadastradas; as mensagens não repetem valor algum.</response>
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

            var created = await _imports.StartAsync(User.GetUserId(), info, ImportRunStartedVia.Screen);

            return AcceptedAtAction(nameof(GetRun), new { runId = created.Id }, created);
        }
        catch (ValidationException ex)
        {
            // Sem log do corpo (FR-003): um LogError com `info` levaria o período inteiro para o stderr
            // e, se alguém reintroduzir um campo de credencial, a senha junto.
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
    /// "Atualizar base" (feature 008, FR-011 a FR-013): continua de onde a última importação parou —
    /// do último pedido importado, inclusive, até hoje. <b>As datas não vêm do corpo</b>: o único campo
    /// é <c>dryRun</c>, e o período é calculado no servidor, o que impede a tela de mandar um intervalo
    /// que a vendedora não escolheu de verdade.
    /// <para>
    /// Quando a base não pode ser atualizada de uma vez (sem pedidos importados, ou mais de
    /// <c>maxRangeMonths</c> para trás), responde <c>400</c> com o mesmo motivo que <c>defaults</c> já
    /// tinha mostrado em <c>updateBase.reason</c> (FR-015).
    /// </para>
    /// </summary>
    /// <param name="info">Só o modo: simulação ou gravação.</param>
    /// <response code="202">Execução criada e na fila (<c>kind: updateBase</c>).</response>
    /// <response code="400">Base indisponível, período acima do limite ou credenciais não cadastradas.</response>
    /// <response code="401">Token ausente, inválido ou expirado.</response>
    /// <response code="409">Já existe importação em andamento.</response>
    /// <response code="429">Bloqueio por recusas seguidas de credencial; ver <c>Retry-After</c>.</response>
    /// <response code="500">Erro interno (mensagem da exceção).</response>
    [HttpPost("runs/update-base")]
    [ProducesResponseType(typeof(ImportRunInfo), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResult), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UpdateBase([FromBody] ImportUpdateBaseInfo? info)
    {
        try
        {
            var created = await _imports.UpdateBaseAsync(
                User.GetUserId(), info?.DryRun ?? false, ImportRunStartedVia.Screen);

            return AcceptedAtAction(nameof(GetRun), new { runId = created.Id }, created);
        }
        catch (ValidationException ex)
        {
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
