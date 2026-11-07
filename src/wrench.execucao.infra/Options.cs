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

public class OutboxOptions
{
    public const string Secao = "Outbox";

    public int IntervaloSegundos { get; set; } = 2;
    public int TamanhoLote { get; set; } = 25;
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
