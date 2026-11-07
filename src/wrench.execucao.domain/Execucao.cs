namespace wrench.execucao.domain;

public class Execucao
{
    private readonly List<ItemExecucao> _itens;

    private Execucao(
        Guid ordemServicoId,
        ExecucaoStatus status,
        string descricao,
        decimal valorAprovado,
        IEnumerable<ItemExecucao> itens,
        DateTime enfileiradaEm,
        DateTime? iniciadaEm,
        DateTime? concluidaEm,
        string? mecanico,
        string? motivo,
        string? correlationId,
        long versao)
    {
        OrdemServicoId = ordemServicoId;
        Status = status;
        Descricao = descricao;
        ValorAprovado = valorAprovado;
        _itens = [.. itens];
        EnfileiradaEm = enfileiradaEm;
        IniciadaEm = iniciadaEm;
        ConcluidaEm = concluidaEm;
        Mecanico = mecanico;
        Motivo = motivo;
        CorrelationId = correlationId;
        Versao = versao;
    }

    public Guid OrdemServicoId { get; }
    public ExecucaoStatus Status { get; private set; }
    public string Descricao { get; }
    public decimal ValorAprovado { get; }
    public IReadOnlyList<ItemExecucao> Itens => _itens.AsReadOnly();
    public DateTime EnfileiradaEm { get; }
    public DateTime? IniciadaEm { get; private set; }
    public DateTime? ConcluidaEm { get; private set; }
    public string? Mecanico { get; private set; }
    public string? Motivo { get; private set; }
    public string? CorrelationId { get; }
    public long Versao { get; private set; }

    public static Execucao Enfileirar(
        Guid ordemServicoId,
        string descricao,
        decimal valorAprovado,
        IEnumerable<ItemExecucao> itens,
        string? correlationId,
        DateTime agora)
    {
        if (ordemServicoId == Guid.Empty)
            throw new DomainException("O identificador da ordem de serviço deve ser informado.");

        if (string.IsNullOrWhiteSpace(descricao))
            throw new DomainException("A descrição da execução deve ser informada.");

        if (valorAprovado < 0)
            throw new DomainException("O valor aprovado deve ser maior ou igual a zero.");

        var itensLista = (itens ?? []).ToList();

        if (itensLista.Any(i => i.Quantidade < 1))
            throw new DomainException("A quantidade mínima de cada item é 1.");

        return new Execucao(
            ordemServicoId, ExecucaoStatus.Pendente, descricao.Trim(), valorAprovado, itensLista,
            agora, null, null, null, null, correlationId, 0);
    }

    public static Execucao Restaurar(
        Guid ordemServicoId,
        ExecucaoStatus status,
        string descricao,
        decimal valorAprovado,
        IEnumerable<ItemExecucao> itens,
        DateTime enfileiradaEm,
        DateTime? iniciadaEm,
        DateTime? concluidaEm,
        string? mecanico,
        string? motivo,
        string? correlationId,
        long versao) =>
        new(ordemServicoId, status, descricao, valorAprovado, itens, enfileiradaEm, iniciadaEm,
            concluidaEm, mecanico, motivo, correlationId, versao);

    public void Assumir(string mecanico, DateTime agora)
    {
        if (Status != ExecucaoStatus.Pendente)
            throw new DomainException("Apenas execuções pendentes podem ser assumidas.");

        if (string.IsNullOrWhiteSpace(mecanico))
            throw new DomainException("O mecânico responsável deve ser informado.");

        Mecanico = mecanico.Trim();
        IniciadaEm = agora;
        Status = ExecucaoStatus.EmExecucao;
    }

    public void Concluir(DateTime agora)
    {
        if (Status != ExecucaoStatus.EmExecucao)
            throw new DomainException("Apenas execuções em andamento podem ser concluídas.");

        ConcluidaEm = agora;
        Status = ExecucaoStatus.Concluida;
    }

    public void Falhar(string motivo)
    {
        if (!PodeFalhar())
            throw new DomainException("Apenas execuções pendentes ou em andamento podem falhar.");

        if (string.IsNullOrWhiteSpace(motivo))
            throw new DomainException("O motivo da falha deve ser informado.");

        Motivo = motivo.Trim();
        Status = ExecucaoStatus.Falhou;
    }

    public void Remover(string motivo)
    {
        if (!PodeSerRemovida())
            throw new DomainException("Apenas execuções pendentes ou em andamento podem ser removidas da fila.");

        if (string.IsNullOrWhiteSpace(motivo))
            throw new DomainException("O motivo da remoção deve ser informado.");

        Motivo = motivo.Trim();
        Status = ExecucaoStatus.Removida;
    }

    public bool PodeFalhar() => Status is ExecucaoStatus.Pendente or ExecucaoStatus.EmExecucao;

    public bool PodeSerRemovida() => Status is ExecucaoStatus.Pendente or ExecucaoStatus.EmExecucao;

    public void AtualizarVersao(long versao) => Versao = versao;
}
