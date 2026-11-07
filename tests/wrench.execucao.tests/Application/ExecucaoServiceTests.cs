using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using wrench.execucao.application;
using wrench.execucao.application.Contracts;
using wrench.execucao.domain;
using wrench.execucao.tests.Support;

namespace wrench.execucao.tests.Application;

public class ExecucaoServiceTests
{
    private readonly FakeExecucaoRepository _repositorio = new();
    private readonly ExecucaoService _servico;

    public ExecucaoServiceTests()
    {
        _servico = new ExecucaoService(_repositorio, new RelogioFixo(new DateTimeOffset(Dados.Agora)), NullLogger<ExecucaoService>.Instance);
    }

    private static IniciarExecucao MensagemIniciar(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "corr-1", "Troca de óleo", [new ItemExecucaoDto(Guid.NewGuid(), "Óleo 5W30", 4)], 280m);

    // ---- Iniciar

    [Fact(DisplayName = "Iniciar deve enfileirar a execução como pendente")]
    [Trait("Execução", "Application")]
    public async Task Iniciar_DeveEnfileirar()
    {
        var mensagem = MensagemIniciar();

        var resultado = await _servico.IniciarAsync("msg-1", mensagem, CancellationToken.None);

        Assert.Equal(TipoResultado.Sucesso, resultado.Tipo);
        var salva = _repositorio.Itens[mensagem.OrdemServicoId];
        Assert.Equal(ExecucaoStatus.Pendente, salva.Status);
        Assert.Equal("corr-1", salva.CorrelationId);
        Assert.Equal(280m, salva.ValorAprovado);
        Assert.Equal(Dados.Agora, salva.EnfileiradaEm);
        Assert.Equal(4, salva.Itens.Single().Quantidade);
    }

    [Fact(DisplayName = "Iniciar deve ignorar mensagem repetida")]
    [Trait("Execução", "Application")]
    public async Task Iniciar_DeveSerIdempotentePorMensagem()
    {
        var mensagem = MensagemIniciar();
        await _servico.IniciarAsync("msg-1", mensagem, CancellationToken.None);
        _repositorio.Itens.Clear();

        var resultado = await _servico.IniciarAsync("msg-1", mensagem, CancellationToken.None);

        Assert.Equal(TipoResultado.Duplicado, resultado.Tipo);
        Assert.True(resultado.Sucesso);
    }

    [Fact(DisplayName = "Iniciar deve ignorar OS que já está na fila")]
    [Trait("Execução", "Application")]
    public async Task Iniciar_DeveIgnorarOrdemJaEnfileirada()
    {
        var mensagem = MensagemIniciar();
        await _servico.IniciarAsync("msg-1", mensagem, CancellationToken.None);

        var resultado = await _servico.IniciarAsync("msg-2", mensagem, CancellationToken.None);

        Assert.Equal(TipoResultado.Duplicado, resultado.Tipo);
        Assert.Single(_repositorio.Itens);
    }

    [Fact(DisplayName = "Iniciar deve tratar corrida de gravação como duplicidade")]
    [Trait("Execução", "Application")]
    public async Task Iniciar_DeveTratarCorridaComoDuplicado()
    {
        _repositorio.ForcarResultado = SalvarResultado.Duplicado;

        var resultado = await _servico.IniciarAsync("msg-1", MensagemIniciar(), CancellationToken.None);

        Assert.Equal(TipoResultado.Duplicado, resultado.Tipo);
    }

    [Fact(DisplayName = "Iniciar deve exigir o identificador da mensagem")]
    [Trait("Execução", "Application")]
    public async Task Iniciar_DeveExigirMessageId()
    {
        var resultado = await _servico.IniciarAsync(" ", MensagemIniciar(), CancellationToken.None);

        Assert.Equal(TipoResultado.Validacao, resultado.Tipo);
    }

    [Fact(DisplayName = "Iniciar deve validar os dados da mensagem")]
    [Trait("Execução", "Application")]
    public async Task Iniciar_DeveValidarDados()
    {
        var invalida = MensagemIniciar() with { Descricao = "" };

        var resultado = await _servico.IniciarAsync("msg-1", invalida, CancellationToken.None);

        Assert.Equal(TipoResultado.Validacao, resultado.Tipo);
        Assert.Empty(_repositorio.Itens);
    }

    [Fact(DisplayName = "Iniciar deve aceitar mensagem sem itens")]
    [Trait("Execução", "Application")]
    public async Task Iniciar_DeveAceitarSemItens()
    {
        var mensagem = MensagemIniciar() with { Itens = null! };

        var resultado = await _servico.IniciarAsync("msg-1", mensagem, CancellationToken.None);

        Assert.Equal(TipoResultado.Sucesso, resultado.Tipo);
    }

    // ---- Cancelar

    [Fact(DisplayName = "Cancelar deve remover a execução da fila")]
    [Trait("Execução", "Application")]
    public async Task Cancelar_DeveRemover()
    {
        var execucao = Dados.NovaExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.CancelarAsync("msg-1", new CancelarExecucao(execucao.OrdemServicoId, "corr", "Pagamento recusado"), CancellationToken.None);

        Assert.Equal(TipoResultado.Sucesso, resultado.Tipo);
        Assert.Equal(ExecucaoStatus.Removida, execucao.Status);
        Assert.Equal("Pagamento recusado", execucao.Motivo);
    }

    [Fact(DisplayName = "Cancelar sem execução existente deve ser um no-op bem-sucedido")]
    [Trait("Execução", "Application")]
    public async Task Cancelar_SemExecucao_DeveSerNoOp()
    {
        var resultado = await _servico.CancelarAsync("msg-1", new CancelarExecucao(Guid.NewGuid(), "corr", "Motivo"), CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Empty(_repositorio.Itens);
    }

    [Theory(DisplayName = "Cancelar execução já encerrada por remoção ou falha deve ser idempotente")]
    [Trait("Execução", "Application")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelar_JaEncerrada_DeveSerDuplicado(bool removida)
    {
        var execucao = removida ? Dados.Removida() : Dados.Falhou();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.CancelarAsync("msg-1", new CancelarExecucao(execucao.OrdemServicoId, "corr", "Motivo"), CancellationToken.None);

        Assert.Equal(TipoResultado.Duplicado, resultado.Tipo);
    }

    [Fact(DisplayName = "Cancelar execução concluída deve retornar conflito")]
    [Trait("Execução", "Application")]
    public async Task Cancelar_Concluida_DeveRetornarConflito()
    {
        var execucao = Dados.Concluida();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.CancelarAsync("msg-1", new CancelarExecucao(execucao.OrdemServicoId, "corr", "Motivo"), CancellationToken.None);

        Assert.Equal(TipoResultado.Conflito, resultado.Tipo);
        Assert.Equal(ExecucaoStatus.Concluida, execucao.Status);
    }

    [Theory(DisplayName = "Cancelar deve validar mensagem e motivo")]
    [Trait("Execução", "Application")]
    [InlineData("", "Motivo")]
    [InlineData("msg-1", " ")]
    public async Task Cancelar_DeveValidar(string messageId, string motivo)
    {
        var resultado = await _servico.CancelarAsync(messageId, new CancelarExecucao(Guid.NewGuid(), "corr", motivo), CancellationToken.None);

        Assert.Equal(TipoResultado.Validacao, resultado.Tipo);
    }

    [Fact(DisplayName = "Cancelar deve retornar conflito quando a versão mudou")]
    [Trait("Execução", "Application")]
    public async Task Cancelar_DeveRetornarConflitoDeVersao()
    {
        var execucao = Dados.NovaExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;
        _repositorio.ForcarResultado = SalvarResultado.ConflitoDeVersao;

        var resultado = await _servico.CancelarAsync("msg-1", new CancelarExecucao(execucao.OrdemServicoId, "corr", "Motivo"), CancellationToken.None);

        Assert.Equal(TipoResultado.Conflito, resultado.Tipo);
    }

    [Fact(DisplayName = "Cancelar mensagem repetida deve ser ignorada")]
    [Trait("Execução", "Application")]
    public async Task Cancelar_MensagemRepetida_DeveSerDuplicado()
    {
        var execucao = Dados.NovaExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;
        _repositorio.MensagensProcessadas.Add("msg-1");

        var resultado = await _servico.CancelarAsync("msg-1", new CancelarExecucao(execucao.OrdemServicoId, "corr", "Motivo"), CancellationToken.None);

        Assert.Equal(TipoResultado.Duplicado, resultado.Tipo);
    }

    // ---- Assumir

    [Fact(DisplayName = "Assumir deve colocar a execução em andamento")]
    [Trait("Execução", "Application")]
    public async Task Assumir_DeveIniciar()
    {
        var execucao = Dados.NovaExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.AssumirAsync(execucao.OrdemServicoId, "Carlos", CancellationToken.None);

        Assert.Equal(TipoResultado.Sucesso, resultado.Tipo);
        Assert.Equal(ExecucaoStatus.EmExecucao, execucao.Status);
        Assert.Equal("Carlos", execucao.Mecanico);
        Assert.Equal(1, execucao.Versao);
    }

    [Fact(DisplayName = "Assumir deve exigir mecânico")]
    [Trait("Execução", "Application")]
    public async Task Assumir_DeveExigirMecanico()
    {
        var resultado = await _servico.AssumirAsync(Guid.NewGuid(), "", CancellationToken.None);

        Assert.Equal(TipoResultado.Validacao, resultado.Tipo);
    }

    [Fact(DisplayName = "Assumir execução inexistente deve retornar não encontrado")]
    [Trait("Execução", "Application")]
    public async Task Assumir_DeveRetornarNaoEncontrado()
    {
        var resultado = await _servico.AssumirAsync(Guid.NewGuid(), "Carlos", CancellationToken.None);

        Assert.Equal(TipoResultado.NaoEncontrado, resultado.Tipo);
    }

    [Fact(DisplayName = "Assumir execução já em andamento deve retornar conflito")]
    [Trait("Execução", "Application")]
    public async Task Assumir_DeveRetornarConflito_QuandoJaEmAndamento()
    {
        var execucao = Dados.EmExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.AssumirAsync(execucao.OrdemServicoId, "Outro", CancellationToken.None);

        Assert.Equal(TipoResultado.Conflito, resultado.Tipo);
    }

    [Fact(DisplayName = "Assumir deve retornar conflito quando a versão mudou")]
    [Trait("Execução", "Application")]
    public async Task Assumir_DeveRetornarConflitoDeVersao()
    {
        var execucao = Dados.NovaExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;
        _repositorio.ForcarResultado = SalvarResultado.ConflitoDeVersao;

        var resultado = await _servico.AssumirAsync(execucao.OrdemServicoId, "Carlos", CancellationToken.None);

        Assert.Equal(TipoResultado.Conflito, resultado.Tipo);
    }

    // ---- Concluir

    [Fact(DisplayName = "Concluir deve finalizar e gravar ExecucaoConcluida no outbox")]
    [Trait("Execução", "Application")]
    public async Task Concluir_DeveFinalizarEPublicar()
    {
        var execucao = Dados.EmExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.ConcluirAsync(execucao.OrdemServicoId, CancellationToken.None);

        Assert.Equal(TipoResultado.Sucesso, resultado.Tipo);
        Assert.Equal(ExecucaoStatus.Concluida, execucao.Status);

        var mensagem = Assert.Single(_repositorio.Outbox);
        Assert.Equal(MensagemTipo.ExecucaoConcluida, mensagem.Tipo);
        Assert.Equal("corr-1", mensagem.CorrelationId);

        using var json = JsonDocument.Parse(mensagem.Payload);
        Assert.Equal(execucao.OrdemServicoId, json.RootElement.GetProperty("ordemServicoId").GetGuid());
        Assert.Equal("corr-1", json.RootElement.GetProperty("correlationId").GetString());
        Assert.True(json.RootElement.TryGetProperty("concluidaEm", out _));
    }

    [Fact(DisplayName = "Concluir execução inexistente deve retornar não encontrado")]
    [Trait("Execução", "Application")]
    public async Task Concluir_DeveRetornarNaoEncontrado()
    {
        var resultado = await _servico.ConcluirAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(TipoResultado.NaoEncontrado, resultado.Tipo);
    }

    [Fact(DisplayName = "Concluir execução não assumida deve retornar conflito sem publicar")]
    [Trait("Execução", "Application")]
    public async Task Concluir_DeveRetornarConflito_QuandoPendente()
    {
        var execucao = Dados.NovaExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.ConcluirAsync(execucao.OrdemServicoId, CancellationToken.None);

        Assert.Equal(TipoResultado.Conflito, resultado.Tipo);
        Assert.Empty(_repositorio.Outbox);
    }

    [Fact(DisplayName = "Concluir deve retornar conflito quando a versão mudou")]
    [Trait("Execução", "Application")]
    public async Task Concluir_DeveRetornarConflitoDeVersao()
    {
        var execucao = Dados.EmExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;
        _repositorio.ForcarResultado = SalvarResultado.ConflitoDeVersao;

        var resultado = await _servico.ConcluirAsync(execucao.OrdemServicoId, CancellationToken.None);

        Assert.Equal(TipoResultado.Conflito, resultado.Tipo);
    }

    [Fact(DisplayName = "Concluir sem correlationId deve publicar correlação vazia")]
    [Trait("Execução", "Application")]
    public async Task Concluir_SemCorrelationId_DevePublicarVazio()
    {
        var execucao = Execucao.Enfileirar(Guid.NewGuid(), "Revisão", 1m, [], null, Dados.Agora);
        execucao.Assumir("Carlos", Dados.Agora);
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        await _servico.ConcluirAsync(execucao.OrdemServicoId, CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(_repositorio.Outbox).Payload);
        Assert.Equal(string.Empty, json.RootElement.GetProperty("correlationId").GetString());
    }

    // ---- Falhar

    [Fact(DisplayName = "Falhar deve marcar a falha e gravar ExecucaoFalhou no outbox")]
    [Trait("Execução", "Application")]
    public async Task Falhar_DeveMarcarEPublicar()
    {
        var execucao = Dados.EmExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.FalharAsync(execucao.OrdemServicoId, "Peça indisponível", CancellationToken.None);

        Assert.Equal(TipoResultado.Sucesso, resultado.Tipo);
        Assert.Equal(ExecucaoStatus.Falhou, execucao.Status);

        var mensagem = Assert.Single(_repositorio.Outbox);
        Assert.Equal(MensagemTipo.ExecucaoFalhou, mensagem.Tipo);

        using var json = JsonDocument.Parse(mensagem.Payload);
        Assert.Equal("Peça indisponível", json.RootElement.GetProperty("motivo").GetString());
    }

    [Fact(DisplayName = "Falhar deve exigir motivo")]
    [Trait("Execução", "Application")]
    public async Task Falhar_DeveExigirMotivo()
    {
        var resultado = await _servico.FalharAsync(Guid.NewGuid(), " ", CancellationToken.None);

        Assert.Equal(TipoResultado.Validacao, resultado.Tipo);
    }

    [Fact(DisplayName = "Falhar execução inexistente deve retornar não encontrado")]
    [Trait("Execução", "Application")]
    public async Task Falhar_DeveRetornarNaoEncontrado()
    {
        var resultado = await _servico.FalharAsync(Guid.NewGuid(), "Motivo", CancellationToken.None);

        Assert.Equal(TipoResultado.NaoEncontrado, resultado.Tipo);
    }

    [Fact(DisplayName = "Falhar execução concluída deve retornar conflito")]
    [Trait("Execução", "Application")]
    public async Task Falhar_DeveRetornarConflito_QuandoConcluida()
    {
        var execucao = Dados.Concluida();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resultado = await _servico.FalharAsync(execucao.OrdemServicoId, "Motivo", CancellationToken.None);

        Assert.Equal(TipoResultado.Conflito, resultado.Tipo);
        Assert.Empty(_repositorio.Outbox);
    }

    // ---- Consultas

    [Fact(DisplayName = "Listar fila deve filtrar por status em ordem de chegada e limitar o resultado")]
    [Trait("Execução", "Application")]
    public async Task ListarFila_DeveFiltrarOrdenarELimitar()
    {
        var maisAntiga = Execucao.Enfileirar(Guid.NewGuid(), "A", 1m, [], null, Dados.Agora);
        var maisNova = Execucao.Enfileirar(Guid.NewGuid(), "B", 1m, [], null, Dados.Agora.AddHours(1));
        var emExecucao = Dados.EmExecucao();
        foreach (var e in new[] { maisNova, maisAntiga, emExecucao })
            _repositorio.Itens[e.OrdemServicoId] = e;

        var pendentes = await _servico.ListarFilaAsync(ExecucaoStatus.Pendente, 10, CancellationToken.None);
        var limitada = await _servico.ListarFilaAsync(ExecucaoStatus.Pendente, 0, CancellationToken.None);

        Assert.Equal([maisAntiga.OrdemServicoId, maisNova.OrdemServicoId], pendentes.Select(p => p.OrdemServicoId));
        Assert.Single(limitada);
    }

    [Fact(DisplayName = "Obter deve devolver a execução ou nulo")]
    [Trait("Execução", "Application")]
    public async Task Obter_DeveDevolverExecucaoOuNulo()
    {
        var execucao = Dados.NovaExecucao();
        _repositorio.Itens[execucao.OrdemServicoId] = execucao;

        Assert.Same(execucao, await _servico.ObterAsync(execucao.OrdemServicoId, CancellationToken.None));
        Assert.Null(await _servico.ObterAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ---- Infra de suporte

    [Fact(DisplayName = "OutboxMessage deve serializar o conteúdo em camelCase")]
    [Trait("Execução", "Application")]
    public void OutboxMessage_DeveSerializarEmCamelCase()
    {
        var id = Guid.NewGuid();

        var mensagem = OutboxMessage.Criar("Tipo", new ExecucaoFalhou(id, "corr", "Motivo"), "corr", Dados.Agora);

        Assert.Equal("Tipo", mensagem.Tipo);
        Assert.Equal(Dados.Agora, mensagem.CriadoEm);
        Assert.Contains("\"ordemServicoId\"", mensagem.Payload);
        Assert.Contains(id.ToString(), mensagem.Payload);
    }

    [Fact(DisplayName = "AddExecucaoApplication deve registrar o serviço")]
    [Trait("Execução", "Application")]
    public void AddExecucaoApplication_DeveRegistrarServico()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IExecucaoRepository>(_repositorio);
        services.AddExecucaoApplication();

        using var provider = services.BuildServiceProvider();
        using var escopo = provider.CreateScope();

        Assert.NotNull(escopo.ServiceProvider.GetRequiredService<ExecucaoService>());
    }

    [Theory(DisplayName = "ResultadoOperacao deve indicar sucesso apenas para Sucesso e Duplicado")]
    [Trait("Execução", "Application")]
    [InlineData(TipoResultado.Sucesso, true)]
    [InlineData(TipoResultado.Duplicado, true)]
    [InlineData(TipoResultado.NaoEncontrado, false)]
    [InlineData(TipoResultado.Conflito, false)]
    [InlineData(TipoResultado.Validacao, false)]
    public void ResultadoOperacao_DeveIndicarSucesso(TipoResultado tipo, bool esperado) =>
        Assert.Equal(esperado, new ResultadoOperacao(tipo).Sucesso);
}
