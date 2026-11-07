using System.Diagnostics;
using System.Text.Json;
using wrench.execucao.domain;

namespace wrench.execucao.application;

public enum SalvarResultado
{
    Salvo,
    Duplicado,
    ConflitoDeVersao
}

public sealed record OutboxMessage(
    Guid Id,
    string Tipo,
    string Payload,
    string? CorrelationId,
    string? Traceparent,
    DateTime CriadoEm)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static OutboxMessage Criar<T>(string tipo, T conteudo, string? correlationId, DateTime agora) =>
        new(Guid.NewGuid(), tipo, JsonSerializer.Serialize(conteudo, JsonOptions), correlationId,
            Activity.Current?.Id, agora);
}

public interface IExecucaoRepository
{
    Task<Execucao?> ObterAsync(Guid ordemServicoId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Execucao>> ListarPorStatusAsync(ExecucaoStatus status, int limite, CancellationToken cancellationToken);

    /// <summary>
    /// Grava a execução, o registro de idempotência da mensagem (quando houver) e as
    /// mensagens de saída (outbox) de forma atômica.
    /// </summary>
    Task<SalvarResultado> SalvarAsync(
        Execucao execucao,
        bool nova,
        string? messageId,
        IReadOnlyList<OutboxMessage> outbox,
        CancellationToken cancellationToken);
}

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> ObterPendentesAsync(int limite, CancellationToken cancellationToken);

    Task MarcarPublicadaAsync(Guid id, CancellationToken cancellationToken);
}

public interface IMessagePublisher
{
    Task PublicarAsync(OutboxMessage mensagem, CancellationToken cancellationToken);
}
