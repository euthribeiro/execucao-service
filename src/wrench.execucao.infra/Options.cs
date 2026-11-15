namespace wrench.execucao.infra;

public class DynamoOptions
{
    public const string Secao = "Dynamo";

    public string Execucao { get; set; } = "wrench-execucao";
    public string Outbox { get; set; } = "wrench-execucao-outbox";
    public string Idempotencia { get; set; } = "wrench-execucao-idempotencia";

    /// <summary>Preenchido apenas em desenvolvimento (DynamoDB Local).</summary>
    public string? ServiceUrl { get; set; }

    public string? Region { get; set; }

    /// <summary>Cria as tabelas na inicialização. Apenas para desenvolvimento/testes locais.</summary>
    public bool CriarTabelas { get; set; }

    public int IdempotenciaDias { get; set; } = 7;
}

public class SnsOptions
{
    public const string Secao = "Sns";

    public string TopicArn { get; set; } = string.Empty;
    public string? ServiceUrl { get; set; }
}

public class SqsOptions
{
    public const string Secao = "Sqs";

    /// <summary>Fila de comandos da Saga. Vazio mantém o consumidor desligado.</summary>
    public string QueueUrl { get; set; } = string.Empty;

    public string? ServiceUrl { get; set; }

    public int MensagensPorLeitura { get; set; } = 10;

    public int EsperaLongaSegundos { get; set; } = 20;
}

public class OutboxOptions
{
    public const string Secao = "Outbox";

    public int IntervaloSegundos { get; set; } = 2;
    public int TamanhoLote { get; set; } = 25;
}

/// <summary>
/// Message attributes que viajam junto do corpo. O <c>tipo</c> é o que o filter policy da
/// assinatura SNS avalia e o que o consumidor usa para rotear.
/// </summary>
public static class AtributosMensagem
{
    public const string MessageId = "messageId";
    public const string Tipo = "tipo";
    public const string CorrelationId = "correlationId";
    public const string Traceparent = "traceparent";
    public const string ContagemDeEntregas = "ApproximateReceiveCount";
}

public static class Indices
{
    public const string ExecucaoPorStatus = "status-enfileiradaEm";
    public const string OutboxPorStatus = "status-criadoEm";
}

public static class OutboxStatus
{
    public const string Pendente = "Pendente";
    public const string Publicada = "Publicada";
}
