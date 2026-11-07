using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using wrench.execucao.application;
using wrench.execucao.domain;
using static wrench.execucao.infra.ExecucaoMapper;

namespace wrench.execucao.infra;

public class DynamoExecucaoRepository(
    IAmazonDynamoDB dynamo,
    IOptions<DynamoOptions> opcoes,
    TimeProvider relogio) : IExecucaoRepository
{
    private const string CondicaoFalhou = "ConditionalCheckFailed";

    private readonly DynamoOptions _opcoes = opcoes.Value;

    public async Task<Execucao?> ObterAsync(Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var resposta = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = _opcoes.Execucao,
            Key = new Dictionary<string, AttributeValue> { ["ordemServicoId"] = S(ordemServicoId.ToString()) },
            ConsistentRead = true
        }, cancellationToken);

        return resposta.Item is { Count: > 0 } item ? ParaDominio(item) : null;
    }

    public async Task<IReadOnlyList<Execucao>> ListarPorStatusAsync(ExecucaoStatus status, int limite, CancellationToken cancellationToken)
    {
        var resposta = await dynamo.QueryAsync(new QueryRequest
        {
            TableName = _opcoes.Execucao,
            IndexName = Indices.ExecucaoPorStatus,
            KeyConditionExpression = "#s = :s",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":s"] = S(status.ToString()) },
            ScanIndexForward = true,
            Limit = limite
        }, cancellationToken);

        return [.. resposta.Items.Select(ParaDominio)];
    }

    public async Task<SalvarResultado> SalvarAsync(
        Execucao execucao,
        bool nova,
        string? messageId,
        IReadOnlyList<OutboxMessage> outbox,
        CancellationToken cancellationToken)
    {
        var novaVersao = execucao.Versao + 1;
        var agora = relogio.GetUtcNow();

        var operacoes = new List<TransactWriteItem>
        {
            new()
            {
                Put = new Put
                {
                    TableName = _opcoes.Execucao,
                    Item = ParaItem(execucao, novaVersao),
                    ConditionExpression = nova ? "attribute_not_exists(ordemServicoId)" : "versao = :v",
                    ExpressionAttributeValues = nova
                        ? null
                        : new Dictionary<string, AttributeValue>
                        {
                            [":v"] = N(execucao.Versao.ToString(CultureInfo.InvariantCulture))
                        }
                }
            }
        };

        var indiceIdempotencia = -1;
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            indiceIdempotencia = operacoes.Count;
            operacoes.Add(new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = _opcoes.Idempotencia,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["messageId"] = S(messageId),
                        ["ordemServicoId"] = S(execucao.OrdemServicoId.ToString()),
                        ["ttl"] = N(agora.AddDays(_opcoes.IdempotenciaDias).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
                    },
                    ConditionExpression = "attribute_not_exists(messageId)"
                }
            });
        }

        foreach (var mensagem in outbox)
        {
            operacoes.Add(new TransactWriteItem { Put = new Put { TableName = _opcoes.Outbox, Item = ParaItemOutbox(mensagem) } });
        }

        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = operacoes }, cancellationToken);
        }
        catch (TransactionCanceledException ex)
        {
            var motivos = ex.CancellationReasons ?? [];

            if (indiceIdempotencia >= 0 && motivos.Count > indiceIdempotencia && motivos[indiceIdempotencia].Code == CondicaoFalhou)
                return SalvarResultado.Duplicado;

            if (motivos.Count > 0 && motivos[0].Code == CondicaoFalhou)
                return nova ? SalvarResultado.Duplicado : SalvarResultado.ConflitoDeVersao;

            throw;
        }

        execucao.AtualizarVersao(novaVersao);
        return SalvarResultado.Salvo;
    }

    internal static Dictionary<string, AttributeValue> ParaItemOutbox(OutboxMessage mensagem)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["id"] = S(mensagem.Id.ToString()),
            ["tipo"] = S(mensagem.Tipo),
            ["payload"] = S(mensagem.Payload),
            ["status"] = S(OutboxStatus.Pendente),
            ["criadoEm"] = S(Data(mensagem.CriadoEm))
        };

        if (!string.IsNullOrEmpty(mensagem.CorrelationId)) item["correlationId"] = S(mensagem.CorrelationId);
        if (!string.IsNullOrEmpty(mensagem.Traceparent)) item["traceparent"] = S(mensagem.Traceparent);

        return item;
    }
}
