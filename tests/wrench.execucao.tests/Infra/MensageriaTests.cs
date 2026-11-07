using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using wrench.execucao.application;
using wrench.execucao.infra;
using wrench.execucao.tests.Support;

namespace wrench.execucao.tests.Infra;

public class MensageriaTests
{
    private static OutboxMessage Mensagem(string? traceparent = "00-abc-def-01", string? correlationId = "corr") =>
        new(Guid.NewGuid(), "ExecucaoConcluida", "{\"ok\":true}", correlationId, traceparent, Dados.Agora);

    // ---- Outbox store

    [Fact(DisplayName = "Outbox deve listar pendentes pelo índice de status")]
    [Trait("Execução", "Infra")]
    public async Task OutboxStore_DeveListarPendentes()
    {
        var mensagem = Mensagem();
        QueryRequest? capturada = null;
        var dynamo = new Mock<IAmazonDynamoDB>();
        dynamo.Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<QueryRequest, CancellationToken>((r, _) => capturada = r)
            .ReturnsAsync(new QueryResponse { Items = [ParaItemOutbox(mensagem)] });
        var store = new DynamoOutboxStore(dynamo.Object, Options.Create(new DynamoOptions()), TimeProvider.System);

        var pendentes = await store.ObterPendentesAsync(10, CancellationToken.None);

        var lida = Assert.Single(pendentes);
        Assert.Equal(mensagem.Id, lida.Id);
        Assert.Equal("corr", lida.CorrelationId);
        Assert.Equal("00-abc-def-01", lida.Traceparent);
        Assert.Equal(Dados.Agora, lida.CriadoEm);
        Assert.Equal(Indices.OutboxPorStatus, capturada!.IndexName);
        Assert.Equal(OutboxStatus.Pendente, capturada.ExpressionAttributeValues[":s"].S);
        Assert.Equal(10, capturada.Limit);
    }

    [Fact(DisplayName = "Outbox deve ler mensagem sem correlação nem traceparent")]
    [Trait("Execução", "Infra")]
    public async Task OutboxStore_DeveLerSemOpcionais()
    {
        var dynamo = new Mock<IAmazonDynamoDB>();
        dynamo.Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse { Items = [ParaItemOutbox(Mensagem(null, null))] });
        var store = new DynamoOutboxStore(dynamo.Object, Options.Create(new DynamoOptions()), TimeProvider.System);

        var lida = Assert.Single(await store.ObterPendentesAsync(5, CancellationToken.None));

        Assert.Null(lida.CorrelationId);
        Assert.Null(lida.Traceparent);
    }

    [Fact(DisplayName = "Outbox deve marcar a mensagem como publicada")]
    [Trait("Execução", "Infra")]
    public async Task OutboxStore_DeveMarcarPublicada()
    {
        var id = Guid.NewGuid();
        UpdateItemRequest? capturada = null;
        var dynamo = new Mock<IAmazonDynamoDB>();
        dynamo.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((r, _) => capturada = r)
            .ReturnsAsync(new UpdateItemResponse());
        var store = new DynamoOutboxStore(dynamo.Object, Options.Create(new DynamoOptions()), new RelogioFixo(new DateTimeOffset(Dados.Agora)));

        await store.MarcarPublicadaAsync(id, CancellationToken.None);

        Assert.Equal(id.ToString(), capturada!.Key["id"].S);
        Assert.Equal(OutboxStatus.Publicada, capturada.ExpressionAttributeValues[":publicada"].S);
        Assert.Equal("attribute_exists(id)", capturada.ConditionExpression);
    }

    // ---- SNS

    [Fact(DisplayName = "Publisher deve publicar no tópico com os atributos da mensagem")]
    [Trait("Execução", "Infra")]
    public async Task Sns_DevePublicarComAtributos()
    {
        var mensagem = Mensagem();
        PublishRequest? capturada = null;
        var sns = new Mock<IAmazonSimpleNotificationService>();
        sns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PublishRequest, CancellationToken>((r, _) => capturada = r)
            .ReturnsAsync(new PublishResponse());
        var publicador = new SnsMessagePublisher(sns.Object, Options.Create(new SnsOptions { TopicArn = "arn:aws:sns:us-east-1:1:execucao" }));

        await publicador.PublicarAsync(mensagem, CancellationToken.None);

        Assert.Equal("arn:aws:sns:us-east-1:1:execucao", capturada!.TopicArn);
        Assert.Equal(mensagem.Payload, capturada.Message);
        Assert.Equal(mensagem.Id.ToString(), capturada.MessageAttributes["messageId"].StringValue);
        Assert.Equal("ExecucaoConcluida", capturada.MessageAttributes["tipo"].StringValue);
        Assert.Equal("corr", capturada.MessageAttributes["correlationId"].StringValue);
        Assert.Equal("00-abc-def-01", capturada.MessageAttributes["traceparent"].StringValue);
    }

    [Fact(DisplayName = "Publisher deve omitir atributos ausentes")]
    [Trait("Execução", "Infra")]
    public async Task Sns_DeveOmitirAtributosAusentes()
    {
        PublishRequest? capturada = null;
        var sns = new Mock<IAmazonSimpleNotificationService>();
        sns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PublishRequest, CancellationToken>((r, _) => capturada = r)
            .ReturnsAsync(new PublishResponse());
        var publicador = new SnsMessagePublisher(sns.Object, Options.Create(new SnsOptions { TopicArn = "arn" }));

        await publicador.PublicarAsync(Mensagem(null, null), CancellationToken.None);

        Assert.False(capturada!.MessageAttributes.ContainsKey("correlationId"));
        Assert.False(capturada.MessageAttributes.ContainsKey("traceparent"));
    }

    [Fact(DisplayName = "Publisher deve falhar quando o tópico não está configurado")]
    [Trait("Execução", "Infra")]
    public async Task Sns_DeveFalharSemTopico()
    {
        var publicador = new SnsMessagePublisher(new Mock<IAmazonSimpleNotificationService>().Object, Options.Create(new SnsOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => publicador.PublicarAsync(Mensagem(), CancellationToken.None));
    }

    // ---- Dispatcher

    [Fact(DisplayName = "Dispatcher deve publicar e marcar as mensagens pendentes")]
    [Trait("Execução", "Infra")]
    public async Task Dispatcher_DevePublicarEMarcar()
    {
        var store = new FakeOutboxStore();
        var publicador = new FakeMessagePublisher();
        store.Pendentes.AddRange([Mensagem(), Mensagem()]);
        var dispatcher = CriarDispatcher(store, publicador);

        var publicadas = await dispatcher.ProcessarLoteAsync(CancellationToken.None);

        Assert.Equal(2, publicadas);
        Assert.Equal(2, publicador.Publicadas.Count);
        Assert.Equal(2, store.Publicadas.Count);
    }

    [Fact(DisplayName = "Dispatcher deve manter pendente a mensagem que falhou e seguir com as demais")]
    [Trait("Execução", "Infra")]
    public async Task Dispatcher_DeveContinuarAposFalha()
    {
        var store = new FakeOutboxStore();
        var publicador = new FakeMessagePublisher();
        var quebrada = Mensagem();
        var boa = Mensagem();
        store.Pendentes.AddRange([quebrada, boa]);
        publicador.Falhar.Add(quebrada.Id);
        var dispatcher = CriarDispatcher(store, publicador);

        var publicadas = await dispatcher.ProcessarLoteAsync(CancellationToken.None);

        Assert.Equal(1, publicadas);
        Assert.Equal([boa.Id], store.Publicadas);
    }

    [Fact(DisplayName = "Dispatcher em execução deve processar o lote e parar ao ser cancelado")]
    [Trait("Execução", "Infra")]
    public async Task Dispatcher_DeveProcessarEParar()
    {
        var store = new FakeOutboxStore();
        var publicador = new FakeMessagePublisher();
        var processou = new TaskCompletionSource();
        store.Pendentes.Add(Mensagem());
        store.AoConsultar = () => processou.TrySetResult();
        var dispatcher = CriarDispatcher(store, publicador);

        await dispatcher.StartAsync(CancellationToken.None);
        await processou.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Single(publicador.Publicadas);
    }

    [Fact(DisplayName = "Dispatcher deve sobreviver a falha na leitura do outbox")]
    [Trait("Execução", "Infra")]
    public async Task Dispatcher_DeveSobreviverAFalhaDeLeitura()
    {
        var tentou = new TaskCompletionSource();
        var outbox = new Mock<IOutboxStore>();
        outbox.Setup(o => o.ObterPendentesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => tentou.TrySetResult())
            .ThrowsAsync(new InvalidOperationException("dynamo fora do ar"));
        var dispatcher = new OutboxDispatcher(
            outbox.Object, new FakeMessagePublisher(),
            Options.Create(new OutboxOptions { IntervaloSegundos = 1, TamanhoLote = 5 }),
            NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        await tentou.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await dispatcher.StopAsync(CancellationToken.None);

        outbox.Verify(o => o.ObterPendentesAsync(5, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private static OutboxDispatcher CriarDispatcher(FakeOutboxStore store, FakeMessagePublisher publicador) =>
        new(store, publicador, Options.Create(new OutboxOptions { IntervaloSegundos = 1, TamanhoLote = 10 }), NullLogger<OutboxDispatcher>.Instance);

    private static Dictionary<string, AttributeValue> ParaItemOutbox(OutboxMessage mensagem) =>
        DynamoExecucaoRepository.ParaItemOutbox(mensagem);
}
