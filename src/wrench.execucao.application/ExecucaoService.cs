using Microsoft.Extensions.Logging;
using wrench.execucao.application.Contracts;
using wrench.execucao.domain;

namespace wrench.execucao.application;

public class ExecucaoService(
    IExecucaoRepository repositorio,
    TimeProvider relogio,
    ILogger<ExecucaoService> logger)
{
    private const string MensagemConflitoVersao =
        "A execução foi alterada por outra operação. Tente novamente.";

    public async Task<ResultadoOperacao> IniciarAsync(string messageId, IniciarExecucao mensagem, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return ResultadoOperacao.Validacao("O identificador da mensagem deve ser informado.");

        var existente = await repositorio.ObterAsync(mensagem.OrdemServicoId, cancellationToken);
        if (existente is not null)
        {
            logger.LogInformation("Execução da OS {OrdemServicoId} já existe; mensagem {MessageId} ignorada",
                mensagem.OrdemServicoId, messageId);
            return ResultadoOperacao.Duplicado("A execução desta ordem de serviço já foi enfileirada.");
        }

        Execucao execucao;
        try
        {
            var itens = (mensagem.Itens ?? []).Select(i => new ItemExecucao(i.PecaId, i.Nome, i.Quantidade));
            execucao = Execucao.Enfileirar(
                mensagem.OrdemServicoId, mensagem.Descricao, mensagem.ValorAprovado, itens,
                mensagem.CorrelationId, relogio.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex)
        {
            return ResultadoOperacao.Validacao(ex.Message);
        }

        var resultado = await repositorio.SalvarAsync(execucao, nova: true, messageId, [], cancellationToken);

        return resultado switch
        {
            SalvarResultado.Salvo => ResultadoOperacao.Ok(),
            _ => ResultadoOperacao.Duplicado("A execução desta ordem de serviço já foi enfileirada.")
        };
    }

    public async Task<ResultadoOperacao> CancelarAsync(string messageId, CancelarExecucao mensagem, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return ResultadoOperacao.Validacao("O identificador da mensagem deve ser informado.");

        if (string.IsNullOrWhiteSpace(mensagem.Motivo))
            return ResultadoOperacao.Validacao("O motivo do cancelamento deve ser informado.");

        var execucao = await repositorio.ObterAsync(mensagem.OrdemServicoId, cancellationToken);

        if (execucao is null)
        {
            logger.LogInformation("Nada a remover para a OS {OrdemServicoId}; mensagem {MessageId} ignorada",
                mensagem.OrdemServicoId, messageId);
            return ResultadoOperacao.Ok("Nenhuma execução encontrada para remover.");
        }

        if (execucao.Status is ExecucaoStatus.Removida or ExecucaoStatus.Falhou)
            return ResultadoOperacao.Duplicado("A execução já está encerrada.");

        if (!execucao.PodeSerRemovida())
            return ResultadoOperacao.Conflito("A execução já foi concluída e não pode ser removida.");

        execucao.Remover(mensagem.Motivo);

        var resultado = await repositorio.SalvarAsync(execucao, nova: false, messageId, [], cancellationToken);

        return MapearSalvar(resultado);
    }

    public async Task<ResultadoOperacao> AssumirAsync(Guid ordemServicoId, string mecanico, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mecanico))
            return ResultadoOperacao.Validacao("O mecânico responsável deve ser informado.");

        var execucao = await repositorio.ObterAsync(ordemServicoId, cancellationToken);
        if (execucao is null)
            return ResultadoOperacao.NaoEncontrado("Execução não encontrada.");

        try
        {
            execucao.Assumir(mecanico, relogio.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex)
        {
            return ResultadoOperacao.Conflito(ex.Message);
        }

        var resultado = await repositorio.SalvarAsync(execucao, nova: false, messageId: null, [], cancellationToken);

        return MapearSalvar(resultado);
    }

    public async Task<ResultadoOperacao> ConcluirAsync(Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var execucao = await repositorio.ObterAsync(ordemServicoId, cancellationToken);
        if (execucao is null)
            return ResultadoOperacao.NaoEncontrado("Execução não encontrada.");

        var agora = relogio.GetUtcNow().UtcDateTime;

        try
        {
            execucao.Concluir(agora);
        }
        catch (DomainException ex)
        {
            return ResultadoOperacao.Conflito(ex.Message);
        }

        var evento = new ExecucaoConcluida(execucao.OrdemServicoId, execucao.CorrelationId ?? string.Empty, agora);
        var outbox = OutboxMessage.Criar(MensagemTipo.ExecucaoConcluida, evento, execucao.CorrelationId, agora);

        var resultado = await repositorio.SalvarAsync(execucao, nova: false, messageId: null, [outbox], cancellationToken);

        return MapearSalvar(resultado);
    }

    public async Task<ResultadoOperacao> FalharAsync(Guid ordemServicoId, string motivo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            return ResultadoOperacao.Validacao("O motivo da falha deve ser informado.");

        var execucao = await repositorio.ObterAsync(ordemServicoId, cancellationToken);
        if (execucao is null)
            return ResultadoOperacao.NaoEncontrado("Execução não encontrada.");

        try
        {
            execucao.Falhar(motivo);
        }
        catch (DomainException ex)
        {
            return ResultadoOperacao.Conflito(ex.Message);
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        var evento = new ExecucaoFalhou(execucao.OrdemServicoId, execucao.CorrelationId ?? string.Empty, execucao.Motivo!);
        var outbox = OutboxMessage.Criar(MensagemTipo.ExecucaoFalhou, evento, execucao.CorrelationId, agora);

        var resultado = await repositorio.SalvarAsync(execucao, nova: false, messageId: null, [outbox], cancellationToken);

        return MapearSalvar(resultado);
    }

    public Task<Execucao?> ObterAsync(Guid ordemServicoId, CancellationToken cancellationToken) =>
        repositorio.ObterAsync(ordemServicoId, cancellationToken);

    public Task<IReadOnlyList<Execucao>> ListarFilaAsync(ExecucaoStatus status, int limite, CancellationToken cancellationToken) =>
        repositorio.ListarPorStatusAsync(status, Math.Clamp(limite, 1, 100), cancellationToken);

    private static ResultadoOperacao MapearSalvar(SalvarResultado resultado) => resultado switch
    {
        SalvarResultado.Salvo => ResultadoOperacao.Ok(),
        SalvarResultado.Duplicado => ResultadoOperacao.Duplicado("Mensagem já processada."),
        _ => ResultadoOperacao.Conflito(MensagemConflitoVersao)
    };
}
