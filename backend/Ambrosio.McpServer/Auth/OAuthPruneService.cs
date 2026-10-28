using OpenIddict.Abstractions;

namespace Ambrosio.McpServer.Auth;

/// <summary>
/// Limpeza periódica das tabelas do OpenIddict (feature 007, T017, data-model §1.3).
/// </summary>
/// <remarks>
/// Sem este serviço, <c>oauth_tokens</c> cresce para sempre: cada login renova o refresh token
/// (R14), e cada rotação deixa para trás uma linha <c>redeemed</c>. Como o MCP valida token pelo
/// <c>aud</c> e pela assinatura — nunca consultando o banco por token — o inchaço não atrapalha a
/// autenticação, mas atrasa o <c>PruneAsync</c> seguinte, incha o backup e enche a tabela de
/// credenciais mortas.
/// <para>
/// O <c>threshold</c> só retira o que já está morto por definição (expirado ou revogado há mais de
/// <see cref="RetentionDays"/> dias): um refresh token dentro da janela de 14 dias continua válido e
/// precisa continuar na base, senão a conversa do dia seguinte não renova.
/// </para>
/// <para>
/// Roda em loop aqui, e não como job externo, porque o host já é um container com ciclo de vida
/// próprio; a API é o único processo que executa importações (ver o comentário dos IHostedService no
/// <c>Program.cs</c>), então não há disputa por esta tarefa.
/// </para>
/// <para>
/// <b>NÃO ESTÁ REGISTRADO no arranque (T017 suspenso).</b> O <c>PruneAsync</c> do OpenIddict 7.7.2
/// chama <c>RelationalQueryableExtensions.ExecuteDeleteAsync</c>, método que existe no EF Core 8.0.30
/// (para onde o asset <c>net8.0</c> do OpenIddict foi compilado) mas saiu desse assembly no EF Core
/// 9.0.22 que o projeto piniza por causa do Npgsql 9 — o resultado é <c>MissingMethodException</c> em
/// 100% dos ciclos, lançada em JIT e invisível no build. A classe fica pronta para o T017 religar o
/// <c>AddHostedService</c> depois de trocar a limpeza por LINQ próprio. Ver o bloqueio em
/// <c>specs/007-chatgpt-mcp-plugin/research.md</c>.
/// </para>
/// </remarks>
public sealed class OAuthPruneService(
    IServiceProvider services,
    ILogger<OAuthPruneService> logger) : BackgroundService
{
    /// <summary>Dias de carência depois de expirar/revogar antes de apagar a linha.</summary>
    public const int RetentionDays = 14;

    /// <summary>Intervalo entre varreduras.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primeira passada um pouco depois do arranque: o seeador de escopos e a janela de conexão
        // do ChatGPT têm prioridade, e varrer logo não ganha nada.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (MissingMethodException ex)
            {
                // Defeito de programação ou de versão de assembly: repetir o ciclo não vai resolver
                // nunca. Subir como Error é o mínimo — foi exatamente um Warning genérico que escondeu
                // esta falha em 100% dos ciclos por várias rodagens.
                logger.LogError(ex,
                    "Limpeza do OpenIddict é incompatível com este assembly do EF Core; encerrando o "
                    + "serviço em vez de insistir no mesmo erro.");
                return;
            }
            catch (Exception ex)
            {
                // transitória (banco indisponível): tokens já emitidos continuam validando por
                // assinatura, e o próximo ciclo tenta de novo.
                logger.LogError(ex,
                    "Limpeza do OpenIddict falhou neste ciclo; nova tentativa em {Interval}.", Interval);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PruneAsync(CancellationToken stoppingToken)
    {
        using var scope = services.CreateScope();

        // Threshold em UTC e com deslocamento explícito: PruneAsync recebe DateTimeOffset e a
        // diferença entre "agora local" e "agora UTC" decidiria quais linhas viviam.
        var threshold = DateTimeOffset.UtcNow.AddDays(-RetentionDays);

        var tokens = await scope.ServiceProvider
            .GetRequiredService<IOpenIddictTokenManager>()
            .PruneAsync(threshold, stoppingToken);

        var authorizations = await scope.ServiceProvider
            .GetRequiredService<IOpenIddictAuthorizationManager>()
            .PruneAsync(threshold, stoppingToken);

        if (tokens > 0 || authorizations > 0)
        {
            logger.LogInformation(
                "Limpeza do OpenIddict: {Tokens} token(s) e {Authorizations} autorização(ões) removidos.",
                tokens, authorizations);
        }
    }
}
