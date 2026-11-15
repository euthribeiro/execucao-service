using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using wrench.execucao.application;
using wrench.execucao.application.Contracts;
using wrench.execucao.infra;
using wrench.execucao.tests.Support;

namespace wrench.execucao.tests.Infra;

public class SqsComandoConsumerTests
{
    private const string Fila = "https://sqs.us-east-1.amazonaws.com/000000000000/wrench-execucao-commands";
    private const string ReceiptHandle = "receipt-handle-1";

    private static readonly Guid OrdemServicoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact(DisplayName = "Sem Sqs:QueueUrl o consumidor nao le a fila")]
    [Trait("Execução", "Consumidor")]
    public async Task ExecuteAsync_DeveEncerrarSemLerAFila_QuandoQueueUrlNaoEstiverConfigurada()
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);

        var consumidor = CriarConsumidor(sqs, new SqsOptions { QueueUrl = string.Empty }, out _);

        await consumidor.StartAsync(CancellationToken.None);
        await consumidor.StopAsync(CancellationToken.None);

        sqs.Verify(c => c.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "IniciarExecucao chega ao servico com o corpo e o messageId")]
    [Trait("Execução", "Consumidor")]
    public async Task ProcessarAsync_DeveRepassarIniciarExecucao_AoServico()
    {
        var apagada = new TaskCompletionSource();

        var comando = new IniciarExecucao(
            OrdemServicoId, "corr-1", "Troca de embreagem",
            [new ItemExecucaoDto(Guid.NewGuid(), "Kit embreagem", 2)], 450m);

        var sqs = CriarSqs(apagada, MensagemDe(nameof(IniciarExecucao), comando));
        var repositorio = new RepositorioEspiao();

        await ExecutarAteAsync(sqs, repositorio, apagada);

        Assert.Equal(OrdemServicoId, repositorio.UltimaExecucaoSalva?.OrdemServicoId);
        Assert.Equal("mensagem-1", repositorio.UltimoMessageId);
        sqs.Verify(c => c.DeleteMessageAsync(Fila, ReceiptHandle, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact(DisplayName = "Mensagem sem o atributo tipo e descartada sem tocar no servico")]
    [Trait("Execução", "Consumidor")]
    public async Task ProcessarAsync_DeveApagarSemDespachar_QuandoMensagemNaoTiverTipo()
    {
        var apagada = new TaskCompletionSource();

        var mensagem = new Message
        {
            MessageId = "sem-tipo",
            Body = "{}",
            ReceiptHandle = ReceiptHandle,
            MessageAttributes = []
        };

        var sqs = CriarSqs(apagada, mensagem);
        var repositorio = new RepositorioEspiao();

        await ExecutarAteAsync(sqs, repositorio, apagada);

        Assert.Null(repositorio.UltimaExecucaoSalva);
        sqs.Verify(c => c.DeleteMessageAsync(Fila, ReceiptHandle, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact(DisplayName = "Tipo desconhecido sai da fila sem erro")]
    [Trait("Execução", "Consumidor")]
    public async Task ProcessarAsync_DeveApagar_QuandoTipoNaoForTratado()
    {
        var apagada = new TaskCompletionSource();

        var sqs = CriarSqs(apagada, MensagemDe("OrcamentoCriado", new { ordemServicoId = OrdemServicoId }));
        var repositorio = new RepositorioEspiao();

        await ExecutarAteAsync(sqs, repositorio, apagada);

        Assert.Null(repositorio.UltimaExecucaoSalva);
        sqs.Verify(c => c.DeleteMessageAsync(Fila, ReceiptHandle, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact(DisplayName = "Falha no repositorio devolve a mensagem para a fila")]
    [Trait("Execução", "Consumidor")]
    public async Task ProcessarAsync_NaoDeveApagar_QuandoOProcessamentoLancar()
    {
        var despachado = new TaskCompletionSource();

        var comando = new IniciarExecucao(OrdemServicoId, "corr-1", "Troca", [], 100m);

        var sqs = CriarSqs(null, MensagemDe(nameof(IniciarExecucao), comando));
        var repositorio = new RepositorioEspiao
        {
            AoSalvar = () =>
            {
                despachado.TrySetResult();
                throw new InvalidOperationException("falha simulada no DynamoDB");
            }
        };

        await ExecutarAteAsync(sqs, repositorio, despachado);

        sqs.Verify(c => c.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "CancelarExecucao sem motivo e recusado e sai da fila")]
    [Trait("Execução", "Consumidor")]
    public async Task ProcessarAsync_DeveApagar_QuandoComandoForRecusadoPorValidacao()
    {
        var apagada = new TaskCompletionSource();

        var comando = new CancelarExecucao(OrdemServicoId, "corr-1", string.Empty);

        var sqs = CriarSqs(apagada, MensagemDe(nameof(CancelarExecucao), comando));
        var repositorio = new RepositorioEspiao();

        await ExecutarAteAsync(sqs, repositorio, apagada);

        Assert.Null(repositorio.UltimaExecucaoSalva);
        sqs.Verify(c => c.DeleteMessageAsync(Fila, ReceiptHandle, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private static Message MensagemDe<T>(string tipo, T conteudo) =>
        new()
        {
            MessageId = "mensagem-1",
            Body = JsonSerializer.Serialize(conteudo, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ReceiptHandle = ReceiptHandle,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                [AtributosMensagem.Tipo] = new() { DataType = "String", StringValue = tipo },
                [AtributosMensagem.MessageId] = new() { DataType = "String", StringValue = "mensagem-1" },
                [AtributosMensagem.CorrelationId] = new() { DataType = "String", StringValue = "corr-1" }
            },
            Attributes = new Dictionary<string, string>
            {
                [AtributosMensagem.ContagemDeEntregas] = "1"
            }
        };

    /// <summary>
    /// Entrega o lote na primeira leitura e vazio nas seguintes, para o laço do consumidor não
    /// girar enquanto o teste espera o sinal.
    /// </summary>
    private static Mock<IAmazonSQS> CriarSqs(TaskCompletionSource? apagada, params Message[] mensagens)
    {
        var sqs = new Mock<IAmazonSQS>();
        var primeiraLeitura = true;

        sqs
            .Setup(c => c.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (!primeiraLeitura) return new ReceiveMessageResponse { Messages = [] };

                primeiraLeitura = false;

                return new ReceiveMessageResponse { Messages = [.. mensagens] };
            });

        sqs
            .Setup(c => c.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse())
            .Callback(() => apagada?.TrySetResult());

        return sqs;
    }

    private static SqsComandoConsumer CriarConsumidor(
        Mock<IAmazonSQS> sqs,
        SqsOptions opcoes,
        out RepositorioEspiao repositorio)
    {
        repositorio = new RepositorioEspiao();
        return CriarConsumidor(sqs, opcoes, repositorio);
    }

    private static SqsComandoConsumer CriarConsumidor(
        Mock<IAmazonSQS> sqs,
        SqsOptions opcoes,
        RepositorioEspiao repositorio)
    {
        var servicos = new ServiceCollection();
        servicos.AddSingleton(TimeProvider.System);
        servicos.AddSingleton<IExecucaoRepository>(repositorio);
        servicos.AddSingleton<ILogger<ExecucaoService>>(NullLogger<ExecucaoService>.Instance);
        servicos.AddScoped<ExecucaoService>();

        var escopos = servicos.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new SqsComandoConsumer(
            sqs.Object,
            escopos,
            Options.Create(opcoes),
            NullLogger<SqsComandoConsumer>.Instance);
    }

    private static async Task ExecutarAteAsync(
        Mock<IAmazonSQS> sqs,
        RepositorioEspiao repositorio,
        TaskCompletionSource sinal)
    {
        var opcoes = new SqsOptions { QueueUrl = Fila, EsperaLongaSegundos = 0 };

        var consumidor = CriarConsumidor(sqs, opcoes, repositorio);

        await consumidor.StartAsync(CancellationToken.None);

        await sinal.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await consumidor.StopAsync(CancellationToken.None);
    }
}
