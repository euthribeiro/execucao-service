using wrench.execucao.application;
using wrench.execucao.domain;

namespace wrench.execucao.tests.Support;

public sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}

public sealed class FakeExecucaoRepository : IExecucaoRepository
{
    public Dictionary<Guid, Execucao> Itens { get; } = [];
    public HashSet<string> MensagensProcessadas { get; } = [];
    public List<OutboxMessage> Outbox { get; } = [];
    public SalvarResultado? ForcarResultado { get; set; }

    public Task<Execucao?> ObterAsync(Guid ordemServicoId, CancellationToken cancellationToken) =>
        Task.FromResult(Itens.GetValueOrDefault(ordemServicoId));

    public Task<IReadOnlyList<Execucao>> ListarPorStatusAsync(ExecucaoStatus status, int limite, CancellationToken cancellationToken)
    {
        IReadOnlyList<Execucao> fila = [.. Itens.Values
            .Where(e => e.Status == status)
            .OrderBy(e => e.EnfileiradaEm)
            .Take(limite)];

        return Task.FromResult(fila);
    }

    public Task<SalvarResultado> SalvarAsync(
        Execucao execucao,
        bool nova,
        string? messageId,
        IReadOnlyList<OutboxMessage> outbox,
        CancellationToken cancellationToken)
    {
        if (ForcarResultado is { } forcado)
            return Task.FromResult(forcado);

        if (messageId is not null && !MensagensProcessadas.Add(messageId))
            return Task.FromResult(SalvarResultado.Duplicado);

        if (nova && Itens.ContainsKey(execucao.OrdemServicoId))
            return Task.FromResult(SalvarResultado.Duplicado);

        Itens[execucao.OrdemServicoId] = execucao;
        execucao.AtualizarVersao(execucao.Versao + 1);
        Outbox.AddRange(outbox);

        return Task.FromResult(SalvarResultado.Salvo);
    }
}

public sealed class FakeOutboxStore : IOutboxStore
{
    public List<OutboxMessage> Pendentes { get; } = [];
    public List<Guid> Publicadas { get; } = [];
    public Action? AoConsultar { get; set; }

    public Task<IReadOnlyList<OutboxMessage>> ObterPendentesAsync(int limite, CancellationToken cancellationToken)
    {
        AoConsultar?.Invoke();
        IReadOnlyList<OutboxMessage> lote = [.. Pendentes.Where(p => !Publicadas.Contains(p.Id)).Take(limite)];
        return Task.FromResult(lote);
    }

    public Task MarcarPublicadaAsync(Guid id, CancellationToken cancellationToken)
    {
        Publicadas.Add(id);
        return Task.CompletedTask;
    }
}

public sealed class FakeMessagePublisher : IMessagePublisher
{
    public List<OutboxMessage> Publicadas { get; } = [];
    public HashSet<Guid> Falhar { get; } = [];

    public Task PublicarAsync(OutboxMessage mensagem, CancellationToken cancellationToken)
    {
        if (Falhar.Contains(mensagem.Id))
            throw new InvalidOperationException("Falha simulada de publicação.");

        Publicadas.Add(mensagem);
        return Task.CompletedTask;
    }
}

public static class Dados
{
    public static readonly DateTime Agora = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    public static Execucao NovaExecucao(Guid? ordemServicoId = null, string? correlationId = "corr-1") =>
        Execucao.Enfileirar(
            ordemServicoId ?? Guid.NewGuid(),
            "Troca de pastilhas de freio",
            450m,
            [new ItemExecucao(Guid.NewGuid(), "Pastilha dianteira", 2)],
            correlationId,
            Agora);

    public static Execucao EmExecucao(Guid? ordemServicoId = null)
    {
        var execucao = NovaExecucao(ordemServicoId);
        execucao.Assumir("Carlos", Agora.AddMinutes(5));
        return execucao;
    }

    public static Execucao Concluida()
    {
        var execucao = EmExecucao();
        execucao.Concluir(Agora.AddHours(1));
        return execucao;
    }

    public static Execucao Falhou()
    {
        var execucao = NovaExecucao();
        execucao.Falhar("Peça indisponível");
        return execucao;
    }

    public static Execucao Removida()
    {
        var execucao = NovaExecucao();
        execucao.Remover("Cancelamento");
        return execucao;
    }
}
