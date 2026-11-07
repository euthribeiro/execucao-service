namespace wrench.execucao.application.Contracts;

// Contratos locais do serviço. Devem ser substituídos pelo pacote compartilhado
// Wrench.Contracts quando a versão 1 for publicada (mudanças apenas aditivas).

public sealed record ItemExecucaoDto(Guid PecaId, string Nome, int Quantidade);

public sealed record IniciarExecucao(
    Guid OrdemServicoId,
    string CorrelationId,
    string Descricao,
    IReadOnlyList<ItemExecucaoDto> Itens,
    decimal ValorAprovado);

public sealed record CancelarExecucao(
    Guid OrdemServicoId,
    string CorrelationId,
    string Motivo);

public sealed record ExecucaoConcluida(
    Guid OrdemServicoId,
    string CorrelationId,
    DateTime ConcluidaEm);

public sealed record ExecucaoFalhou(
    Guid OrdemServicoId,
    string CorrelationId,
    string Motivo);

public static class MensagemTipo
{
    public const string ExecucaoConcluida = nameof(ExecucaoConcluida);
    public const string ExecucaoFalhou = nameof(ExecucaoFalhou);
}
