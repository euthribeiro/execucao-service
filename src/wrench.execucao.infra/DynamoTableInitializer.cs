using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace wrench.execucao.infra;

/// <summary>
/// Cria as tabelas quando elas não existem. Uso restrito a desenvolvimento local
/// (DynamoDB Local); em AWS as tabelas são provisionadas via Terraform.
/// </summary>
public class DynamoTableInitializer(
    IAmazonDynamoDB dynamo,
    IOptions<DynamoOptions> opcoes,
    ILogger<DynamoTableInitializer> logger) : IHostedService
{
    private readonly DynamoOptions _opcoes = opcoes.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_opcoes.CriarTabelas) return;

        await GarantirTabelaAsync(
            _opcoes.Execucao, "ordemServicoId", Indices.ExecucaoPorStatus, "status", "enfileiradaEm", cancellationToken);
        await GarantirTabelaAsync(
            _opcoes.Outbox, "id", Indices.OutboxPorStatus, "status", "criadoEm", cancellationToken);
        await GarantirTabelaAsync(
            _opcoes.Idempotencia, "messageId", null, null, null, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task GarantirTabelaAsync(
        string tabela,
        string chave,
        string? indice,
        string? chaveIndice,
        string? ordenacaoIndice,
        CancellationToken cancellationToken)
    {
        try
        {
            await dynamo.DescribeTableAsync(tabela, cancellationToken);
            return;
        }
        catch (ResourceNotFoundException)
        {
        }

        var requisicao = new CreateTableRequest
        {
            TableName = tabela,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            KeySchema = [new KeySchemaElement(chave, KeyType.HASH)],
            AttributeDefinitions = [new AttributeDefinition(chave, ScalarAttributeType.S)]
        };

        if (indice is not null)
        {
            requisicao.AttributeDefinitions.Add(new AttributeDefinition(chaveIndice, ScalarAttributeType.S));
            requisicao.AttributeDefinitions.Add(new AttributeDefinition(ordenacaoIndice, ScalarAttributeType.S));
            requisicao.GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = indice,
                    KeySchema =
                    [
                        new KeySchemaElement(chaveIndice, KeyType.HASH),
                        new KeySchemaElement(ordenacaoIndice, KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                }
            ];
        }

        await dynamo.CreateTableAsync(requisicao, cancellationToken);
        logger.LogInformation("Tabela DynamoDB {Tabela} criada", tabela);

        if (tabela == _opcoes.Idempotencia)
        {
            await dynamo.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
            {
                TableName = tabela,
                TimeToLiveSpecification = new TimeToLiveSpecification { AttributeName = "ttl", Enabled = true }
            }, cancellationToken);
        }
    }
}
