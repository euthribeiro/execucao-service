using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace wrench.execucao.infra;

public class DynamoHealthCheck(IAmazonDynamoDB dynamo, IOptions<DynamoOptions> opcoes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var resposta = await dynamo.DescribeTableAsync(opcoes.Value.Execucao, cancellationToken);

            return resposta.Table.TableStatus == TableStatus.ACTIVE
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded($"Tabela em status {resposta.Table.TableStatus}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Falha ao acessar o DynamoDB.", ex);
        }
    }
}
