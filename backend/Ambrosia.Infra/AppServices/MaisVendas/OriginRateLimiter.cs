using Ambrosia.DTO.Settings;
using Microsoft.Extensions.Options;

namespace Ambrosia.Infra.AppServices.MaisVendas;

/// <summary>
/// Limitador de ritmo da coleta (feature 006, FR-043, research §9): no máximo ~1,5 requisição por
/// segundo na origem, com uma pausa aleatória antes de cada ação.
/// <para>
/// O +Vendas é o sistema pelo qual a vendedora trabalha: raspá-lo rápido demais degrada a experiência
/// dela e dos colegas, e é o tipo de coisa que derruba um acesso. Um token-bucket de um token a cada
/// 650 ms dá o teto (≤ 2 req/s), e a pausa aleatória de <c>MinDelayMs</c>–<c>MaxDelayMs</c> tira o
/// padrão de máquina do tráfego.
/// </para>
/// <para>
/// É <b>compartilhado</b> entre a navegação, as respostas interceptadas e o download das fotos: se a
/// foto consumisse um bucket próprio, o ritmo real dobraria e a promessa da spec estaria errada.
/// </para>
/// </summary>
public class OriginRateLimiter
{
    /// <summary>Intervalo entre tokens: um a cada 650 ms (≤ 2 req/s com folga).</summary>
    private static readonly TimeSpan TokenInterval = TimeSpan.FromMilliseconds(650);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MaisVendasSettings _settings;
    private DateTime _nextTokenAt = DateTime.UtcNow;

    public OriginRateLimiter(IOptions<MaisVendasSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>
    /// Espera até poder tocar na origem: primeiro o token do bucket, depois a pausa aleatória.
    /// Chamado antes de cada navegação, clique, leitura de resposta e download de foto.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var now = DateTime.UtcNow;

            if (_nextTokenAt > now)
            {
                await Task.Delay(_nextTokenAt - now, cancellationToken);
            }

            // Depois da espera, "agora" já mudou: recalcula a partir do instante atual, não do antigo.
            _nextTokenAt = DateTime.UtcNow.Add(TokenInterval);
        }
        finally
        {
            _gate.Release();
        }

        await JitterAsync(cancellationToken);
    }

    /// <summary>
    /// Pausa aleatória entre o mínimo e o máximo configurados. Usa <see cref="Random.Shared"/>: é
    /// justamente o caso de uso dele (espalhamento, não sigilo), e sortear por execução de coletor não
    /// tem custo de contenção relevante.
    /// </summary>
    private Task JitterAsync(CancellationToken cancellationToken)
    {
        var min = Math.Max(0, _settings.MinDelayMs);
        var max = Math.Max(min, _settings.MaxDelayMs);

        // NextInt(min, max+1): o máximo configurado é alcançável (a faixa do startup é 100..5000).
        var delayMs = min == max ? min : Random.Shared.Next(min, max + 1);

        return delayMs == 0 ? Task.CompletedTask : Task.Delay(delayMs, cancellationToken);
    }
}
