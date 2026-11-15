using wrench.execucao.application;
using wrench.execucao.domain;

namespace wrench.execucao.tests.Support;

/// <summary>
/// Repositório em memória que registra o que foi salvo, para os testes do consumidor verificarem
/// o que chegou da fila sem subir DynamoDB.
/// </summary>
public sealed class RepositorioEspiao : IExecucaoRepository
{
    private readonly Dictionary<Guid, Execucao> _execucoes = [];

    public Execucao? UltimaExecucaoSalva { get; private set; }

    public string? UltimoMessageId { get; private set; }

    public Action? AoSalvar { get; set; }

    public Task<Execucao?> ObterAsync(Guid ordemServicoId, CancellationToken cancellationToken) =>
        Task.FromResult(_execucoes.TryGetValue(ordemServicoId, out var execucao) ? execucao : null);

    public Task<IReadOnlyList<Execucao>> ListarPorStatusAsync(
        ExecucaoStatus status, int limite, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Execucao>>(
            [.. _execucoes.Values.Where(e => e.Status == status).Take(limite)]);

    public Task<SalvarResultado> SalvarAsync(
        Execucao execucao,
        bool nova,
        string? messageId,
        IReadOnlyList<OutboxMessage> outbox,
        CancellationToken cancellationToken)
    {
        AoSalvar?.Invoke();

        _execucoes[execucao.OrdemServicoId] = execucao;
        UltimaExecucaoSalva = execucao;
        UltimoMessageId = messageId;

        return Task.FromResult(SalvarResultado.Salvo);
    }
}
