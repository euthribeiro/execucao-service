using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using wrench.execucao.application;
using wrench.execucao.application.Contracts;

namespace wrench.execucao.infra;

/// <summary>
/// Lê a fila de comandos da Saga por long polling e entrega cada mensagem ao
/// <see cref="ExecucaoService"/>. A mensagem só é apagada quando o resultado é definitivo; qualquer
/// exceção a devolve para a fila e, esgotadas as tentativas, o SQS a move para a DLQ.
/// </summary>
public class SqsComandoConsumer(
    IAmazonSQS sqs,
    IServiceScopeFactory escopos,
    IOptions<SqsOptions> opcoes,
    ILogger<SqsComandoConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SqsOptions _opcoes = opcoes.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.QueueUrl))
        {
            logger.LogWarning("A fila de comandos (Sqs:QueueUrl) nao foi configurada; o consumidor nao sera iniciado.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumirLoteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception excecao)
            {
                logger.LogError(excecao, "Falha ao ler a fila de comandos; nova tentativa em instantes.");

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumirLoteAsync(CancellationToken cancellationToken)
    {
        var resposta = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _opcoes.QueueUrl,
            MaxNumberOfMessages = _opcoes.MensagensPorLeitura,
            WaitTimeSeconds = _opcoes.EsperaLongaSegundos,
            MessageAttributeNames = ["All"],
            MessageSystemAttributeNames = [AtributosMensagem.ContagemDeEntregas]
        }, cancellationToken);

        foreach (var mensagem in resposta.Messages ?? [])
        {
            await ProcessarAsync(mensagem, cancellationToken);
        }
    }

    private async Task ProcessarAsync(Message mensagem, CancellationToken cancellationToken)
    {
        var recebida = ConversorMensagemSqs.Converter(mensagem);

        if (recebida is null)
        {
            logger.LogWarning("Mensagem {MessageId} sem o atributo tipo; removida da fila.", mensagem.MessageId);
            await ApagarAsync(mensagem, cancellationToken);
            return;
        }

        try
        {
            var resultado = await DespacharAsync(recebida, cancellationToken);

            if (resultado is null)
            {
                logger.LogWarning(
                    "Tipo {Tipo} nao e tratado por este servico; mensagem {MessageId} removida da fila.",
                    recebida.Tipo, recebida.MessageId);

                await ApagarAsync(mensagem, cancellationToken);
                return;
            }

            if (!resultado.Sucesso)
            {
                logger.LogWarning(
                    "Mensagem {Tipo} ({MessageId}) recusada como {Resultado}: {Motivo}. Removida da fila, porque reentregar nao muda a decisao.",
                    recebida.Tipo, recebida.MessageId, resultado.Tipo, resultado.Mensagem);
            }

            await ApagarAsync(mensagem, cancellationToken);
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao,
                "Falha ao processar a mensagem {Tipo} ({MessageId}) na tentativa {Tentativa}; voltara para a fila.",
                recebida.Tipo, recebida.MessageId, recebida.TentativasDeEntrega);
        }
    }

    private async Task<ResultadoOperacao?> DespacharAsync(MensagemRecebida recebida, CancellationToken cancellationToken)
    {
        using var escopo = escopos.CreateScope();

        var servico = escopo.ServiceProvider.GetRequiredService<ExecucaoService>();

        switch (recebida.Tipo)
        {
            case nameof(IniciarExecucao):
                var iniciar = Desserializar<IniciarExecucao>(recebida);
                return await servico.IniciarAsync(recebida.MessageId, iniciar, cancellationToken);

            case nameof(CancelarExecucao):
                var cancelar = Desserializar<CancelarExecucao>(recebida);
                return await servico.CancelarAsync(recebida.MessageId, cancelar, cancellationToken);

            default:
                return null;
        }
    }

    private static T Desserializar<T>(MensagemRecebida recebida) =>
        JsonSerializer.Deserialize<T>(recebida.Corpo, Json)
        ?? throw new InvalidOperationException($"Corpo vazio para a mensagem {recebida.Tipo} ({recebida.MessageId}).");

    private Task ApagarAsync(Message mensagem, CancellationToken cancellationToken) =>
        sqs.DeleteMessageAsync(_opcoes.QueueUrl, mensagem.ReceiptHandle, cancellationToken);
}
