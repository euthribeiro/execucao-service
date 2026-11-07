using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using wrench.execucao.application;
using wrench.execucao.infra;

namespace wrench.execucao.tests.Infra;

public class InfraConfiguracaoTests
{
    // ---- Criação de tabelas (desenvolvimento local)

    [Fact(DisplayName = "Inicializador não deve tocar no DynamoDB quando CriarTabelas está desligado")]
    [Trait("Execução", "Infra")]
    public async Task Inicializador_Desligado_NaoFazNada()
    {
        var dynamo = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        var inicializador = new DynamoTableInitializer(dynamo.Object, Options.Create(new DynamoOptions { CriarTabelas = false }), NullLogger<DynamoTableInitializer>.Instance);

        await inicializador.StartAsync(CancellationToken.None);
        await inicializador.StopAsync(CancellationToken.None);

        dynamo.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Inicializador deve criar as três tabelas ausentes, com índices e TTL de idempotência")]
    [Trait("Execução", "Infra")]
    public async Task Inicializador_DeveCriarTabelasAusentes()
    {
        var criadas = new List<CreateTableRequest>();
        var dynamo = new Mock<IAmazonDynamoDB>();
        dynamo.Setup(d => d.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("não existe"));
        dynamo.Setup(d => d.CreateTableAsync(It.IsAny<CreateTableRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTableRequest, CancellationToken>((r, _) => criadas.Add(r))
            .ReturnsAsync(new CreateTableResponse());
        dynamo.Setup(d => d.UpdateTimeToLiveAsync(It.IsAny<UpdateTimeToLiveRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateTimeToLiveResponse());
        var opcoes = new DynamoOptions { CriarTabelas = true };
        var inicializador = new DynamoTableInitializer(dynamo.Object, Options.Create(opcoes), NullLogger<DynamoTableInitializer>.Instance);

        await inicializador.StartAsync(CancellationToken.None);

        Assert.Equal([opcoes.Execucao, opcoes.Outbox, opcoes.Idempotencia], criadas.Select(c => c.TableName));
        Assert.All(criadas, c => Assert.Equal(BillingMode.PAY_PER_REQUEST, c.BillingMode));

        var execucao = criadas[0];
        Assert.Equal(Indices.ExecucaoPorStatus, Assert.Single(execucao.GlobalSecondaryIndexes).IndexName);
        Assert.Contains(execucao.AttributeDefinitions, a => a.AttributeName == "enfileiradaEm");
        Assert.Equal(Indices.OutboxPorStatus, Assert.Single(criadas[1].GlobalSecondaryIndexes).IndexName);
        Assert.Empty(criadas[2].GlobalSecondaryIndexes ?? []);

        dynamo.Verify(d => d.UpdateTimeToLiveAsync(
            It.Is<UpdateTimeToLiveRequest>(r => r.TableName == opcoes.Idempotencia && r.TimeToLiveSpecification.AttributeName == "ttl"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Inicializador não deve recriar tabelas existentes")]
    [Trait("Execução", "Infra")]
    public async Task Inicializador_NaoRecriaTabelasExistentes()
    {
        var dynamo = new Mock<IAmazonDynamoDB>();
        dynamo.Setup(d => d.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeTableResponse());
        var inicializador = new DynamoTableInitializer(dynamo.Object, Options.Create(new DynamoOptions { CriarTabelas = true }), NullLogger<DynamoTableInitializer>.Instance);

        await inicializador.StartAsync(CancellationToken.None);

        dynamo.Verify(d => d.CreateTableAsync(It.IsAny<CreateTableRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Health check

    [Theory(DisplayName = "Health check deve refletir o status da tabela")]
    [Trait("Execução", "Infra")]
    [InlineData("ACTIVE", HealthStatus.Healthy)]
    [InlineData("CREATING", HealthStatus.Degraded)]
    public async Task HealthCheck_DeveRefletirStatusDaTabela(string status, HealthStatus esperado)
    {
        var dynamo = new Mock<IAmazonDynamoDB>();
        dynamo.Setup(d => d.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = new TableStatus(status) } });
        var verificacao = new DynamoHealthCheck(dynamo.Object, Options.Create(new DynamoOptions()));

        var resultado = await verificacao.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(esperado, resultado.Status);
    }

    [Fact(DisplayName = "Health check deve ficar não saudável quando o DynamoDB falha")]
    [Trait("Execução", "Infra")]
    public async Task HealthCheck_DeveFicarNaoSaudavelEmFalha()
    {
        var dynamo = new Mock<IAmazonDynamoDB>();
        dynamo.Setup(d => d.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sem conexão"));
        var verificacao = new DynamoHealthCheck(dynamo.Object, Options.Create(new DynamoOptions()));

        var resultado = await verificacao.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, resultado.Status);
    }

    // ---- Registro de dependências

    [Theory(DisplayName = "AddExecucaoInfra deve registrar repositório, outbox, publicador e serviços hospedados")]
    [Trait("Execução", "Infra")]
    [InlineData(true)]
    [InlineData(false)]
    public void AddExecucaoInfra_DeveRegistrarDependencias(bool endpointLocal)
    {
        var valores = new Dictionary<string, string?>
        {
            ["Dynamo:Execucao"] = "tabela-execucao",
            ["Sns:TopicArn"] = "arn:aws:sns:us-east-1:1:topico",
            ["AWS:Region"] = "us-east-1"
        };
        if (endpointLocal)
        {
            valores["Dynamo:ServiceUrl"] = "http://localhost:8000";
            valores["Sns:ServiceUrl"] = "http://localhost:4566";
        }

        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExecucaoInfra(configuracao);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<DynamoExecucaoRepository>(provider.GetRequiredService<IExecucaoRepository>());
        Assert.IsType<DynamoOutboxStore>(provider.GetRequiredService<IOutboxStore>());
        Assert.IsType<SnsMessagePublisher>(provider.GetRequiredService<IMessagePublisher>());
        Assert.NotNull(provider.GetRequiredService<IAmazonDynamoDB>());
        Assert.Equal("tabela-execucao", provider.GetRequiredService<IOptions<DynamoOptions>>().Value.Execucao);

        var hospedados = provider.GetServices<IHostedService>().Select(h => h.GetType()).ToList();
        Assert.Contains(typeof(OutboxDispatcher), hospedados);
        Assert.Contains(typeof(DynamoTableInitializer), hospedados);
    }

    [Fact(DisplayName = "AddExecucaoInfra deve funcionar com configuração vazia usando os padrões")]
    [Trait("Execução", "Infra")]
    public void AddExecucaoInfra_DeveUsarPadroes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExecucaoInfra(new ConfigurationBuilder().Build());

        var opcoes = services.BuildServiceProvider().GetRequiredService<IOptions<DynamoOptions>>().Value;

        Assert.Equal("wrench-execucao", opcoes.Execucao);
        Assert.Equal("wrench-execucao-outbox", opcoes.Outbox);
        Assert.Equal("wrench-execucao-idempotencia", opcoes.Idempotencia);
        Assert.False(opcoes.CriarTabelas);
    }
}
