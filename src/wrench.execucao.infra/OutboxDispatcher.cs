using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using wrench.execucao.application;

namespace wrench.execucao.infra;

public class OutboxDispatcher(
    IOutboxStore outbox,
    IMessagePublisher publicador,
    IOptions<OutboxOptions> opcoes,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly OutboxOptions _opcoes = opcoes.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(1, _opcoes.IntervaloSegundos));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarLoteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao ler as mensagens pendentes do outbox");
            }

            try
            {
                await Task.Delay(intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Publica as mensagens pendentes e devolve quantas foram publicadas.</summary>
    public async Task<int> ProcessarLoteAsync(CancellationToken cancellationToken)
    {
        var pendentes = await outbox.ObterPendentesAsync(Math.Max(1, _opcoes.TamanhoLote), cancellationToken);
        var publicadas = 0;

        foreach (var mensagem in pendentes)
        {
            try
            {
                await publicador.PublicarAsync(mensagem, cancellationToken);
                await outbox.MarcarPublicadaAsync(mensagem.Id, cancellationToken);
                publicadas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A mensagem continua pendente e será tentada de novo no próximo ciclo.
                logger.LogError(ex, "Falha ao publicar a mensagem {OutboxId} ({Tipo})", mensagem.Id, mensagem.Tipo);
            }
        }

        return publicadas;
    }
}
