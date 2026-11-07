using wrench.execucao.domain;

namespace wrench.execucao.api.Models;

public sealed record ItemExecucaoResponse(Guid PecaId, string Nome, int Quantidade);

public sealed record ExecucaoResponse(
    Guid OrdemServicoId,
    string Status,
    string Descricao,
    decimal ValorAprovado,
    IReadOnlyList<ItemExecucaoResponse> Itens,
    DateTime EnfileiradaEm,
    DateTime? IniciadaEm,
    DateTime? ConcluidaEm,
    string? Mecanico,
    string? Motivo,
    string? CorrelationId)
{
    public static ExecucaoResponse De(Execucao execucao) =>
        new(
            execucao.OrdemServicoId,
            execucao.Status.ToString(),
            execucao.Descricao,
            execucao.ValorAprovado,
            [.. execucao.Itens.Select(i => new ItemExecucaoResponse(i.PecaId, i.Nome, i.Quantidade))],
            execucao.EnfileiradaEm,
            execucao.IniciadaEm,
            execucao.ConcluidaEm,
            execucao.Mecanico,
            execucao.Motivo,
            execucao.CorrelationId);
}

/// <summary>Mecânico responsável. Se omitido, é usado o nome do usuário autenticado.</summary>
public sealed record AssumirExecucaoRequest(string? Mecanico);

public sealed record FalharExecucaoRequest(string Motivo);
