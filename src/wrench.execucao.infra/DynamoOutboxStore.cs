using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using wrench.execucao.application;
using static wrench.execucao.infra.ExecucaoMapper;

namespace wrench.execucao.infra;

public class DynamoOutboxStore(
    IAmazonDynamoDB dynamo,
    IOptions<DynamoOptions> opcoes,
    TimeProvider relogio) : IOutboxStore
{
    private readonly DynamoOptions _opcoes = opcoes.Value;

    public async Task<IReadOnlyList<OutboxMessage>> ObterPendentesAsync(int limite, CancellationToken cancellationToken)
    {
        var resposta = await dynamo.QueryAsync(new QueryRequest
        {
            TableName = _opcoes.Outbox,
            IndexName = Indices.OutboxPorStatus,
            KeyConditionExpression = "#s = :s",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":s"] = S(OutboxStatus.Pendente) },
            ScanIndexForward = true,
            Limit = limite
        }, cancellationToken);

        return [.. resposta.Items.Select(ParaMensagem)];
    }

    public async Task MarcarPublicadaAsync(Guid id, CancellationToken cancellationToken)
    {
        await dynamo.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _opcoes.Outbox,
            Key = new Dictionary<string, AttributeValue> { ["id"] = S(id.ToString()) },
            UpdateExpression = "SET #s = :publicada, publicadoEm = :agora",
            ConditionExpression = "attribute_exists(id)",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":publicada"] = S(OutboxStatus.Publicada),
                [":agora"] = S(Data(relogio.GetUtcNow().UtcDateTime))
            }
        }, cancellationToken);
    }

    private static OutboxMessage ParaMensagem(Dictionary<string, AttributeValue> item) =>
        new(
            Guid.Parse(item["id"].S),
            item["tipo"].S,
            item["payload"].S,
            item.TryGetValue("correlationId", out var correlacao) ? correlacao.S : null,
            item.TryGetValue("traceparent", out var traceparent) ? traceparent.S : null,
            LerData(item["criadoEm"].S));
}
