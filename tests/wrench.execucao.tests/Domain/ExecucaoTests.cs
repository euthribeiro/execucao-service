using wrench.execucao.domain;
using wrench.execucao.tests.Support;

namespace wrench.execucao.tests.Domain;

public class ExecucaoTests
{
    [Fact(DisplayName = "Enfileirar deve criar a execução pendente")]
    [Trait("Execução", "Domain")]
    public void Enfileirar_DeveCriarPendente()
    {
        var id = Guid.NewGuid();

        var execucao = Execucao.Enfileirar(id, "  Alinhamento  ", 120m, [new ItemExecucao(Guid.NewGuid(), "Peso", 1)], "corr", Dados.Agora);

        Assert.Equal(id, execucao.OrdemServicoId);
        Assert.Equal(ExecucaoStatus.Pendente, execucao.Status);
        Assert.Equal("Alinhamento", execucao.Descricao);
        Assert.Equal(120m, execucao.ValorAprovado);
        Assert.Single(execucao.Itens);
        Assert.Equal(Dados.Agora, execucao.EnfileiradaEm);
        Assert.Equal("corr", execucao.CorrelationId);
        Assert.Equal(0, execucao.Versao);
        Assert.Null(execucao.Mecanico);
    }

    [Fact(DisplayName = "Enfileirar sem itens deve aceitar lista vazia")]
    [Trait("Execução", "Domain")]
    public void Enfileirar_DeveAceitarSemItens()
    {
        var execucao = Execucao.Enfileirar(Guid.NewGuid(), "Revisão", 0m, [], null, Dados.Agora);

        Assert.Empty(execucao.Itens);
    }

    [Fact(DisplayName = "Enfileirar deve rejeitar identificador vazio")]
    [Trait("Execução", "Domain")]
    public void Enfileirar_DeveRejeitarIdVazio() =>
        Assert.Throws<DomainException>(() => Execucao.Enfileirar(Guid.Empty, "Revisão", 1m, [], null, Dados.Agora));

    [Theory(DisplayName = "Enfileirar deve rejeitar descrição vazia")]
    [Trait("Execução", "Domain")]
    [InlineData("")]
    [InlineData("   ")]
    public void Enfileirar_DeveRejeitarDescricaoVazia(string descricao) =>
        Assert.Throws<DomainException>(() => Execucao.Enfileirar(Guid.NewGuid(), descricao, 1m, [], null, Dados.Agora));

    [Fact(DisplayName = "Enfileirar deve rejeitar valor negativo")]
    [Trait("Execução", "Domain")]
    public void Enfileirar_DeveRejeitarValorNegativo() =>
        Assert.Throws<DomainException>(() => Execucao.Enfileirar(Guid.NewGuid(), "Revisão", -1m, [], null, Dados.Agora));

    [Fact(DisplayName = "Enfileirar deve rejeitar item com quantidade menor que 1")]
    [Trait("Execução", "Domain")]
    public void Enfileirar_DeveRejeitarQuantidadeInvalida() =>
        Assert.Throws<DomainException>(() =>
            Execucao.Enfileirar(Guid.NewGuid(), "Revisão", 1m, [new ItemExecucao(Guid.NewGuid(), "Filtro", 0)], null, Dados.Agora));

    [Fact(DisplayName = "Assumir deve iniciar a execução pendente")]
    [Trait("Execução", "Domain")]
    public void Assumir_DeveIniciar()
    {
        var execucao = Dados.NovaExecucao();

        execucao.Assumir("  Carlos ", Dados.Agora.AddMinutes(10));

        Assert.Equal(ExecucaoStatus.EmExecucao, execucao.Status);
        Assert.Equal("Carlos", execucao.Mecanico);
        Assert.Equal(Dados.Agora.AddMinutes(10), execucao.IniciadaEm);
    }

    [Fact(DisplayName = "Assumir deve exigir mecânico")]
    [Trait("Execução", "Domain")]
    public void Assumir_DeveExigirMecanico() =>
        Assert.Throws<DomainException>(() => Dados.NovaExecucao().Assumir(" ", Dados.Agora));

    [Fact(DisplayName = "Assumir deve falhar fora do status pendente")]
    [Trait("Execução", "Domain")]
    public void Assumir_DeveFalharQuandoNaoPendente() =>
        Assert.Throws<DomainException>(() => Dados.EmExecucao().Assumir("Outro", Dados.Agora));

    [Fact(DisplayName = "Concluir deve finalizar a execução em andamento")]
    [Trait("Execução", "Domain")]
    public void Concluir_DeveFinalizar()
    {
        var execucao = Dados.EmExecucao();

        execucao.Concluir(Dados.Agora.AddHours(2));

        Assert.Equal(ExecucaoStatus.Concluida, execucao.Status);
        Assert.Equal(Dados.Agora.AddHours(2), execucao.ConcluidaEm);
    }

    [Fact(DisplayName = "Concluir deve falhar quando a execução não foi assumida")]
    [Trait("Execução", "Domain")]
    public void Concluir_DeveFalharQuandoPendente() =>
        Assert.Throws<DomainException>(() => Dados.NovaExecucao().Concluir(Dados.Agora));

    [Theory(DisplayName = "Falhar deve funcionar para pendente e em execução")]
    [Trait("Execução", "Domain")]
    [InlineData(false)]
    [InlineData(true)]
    public void Falhar_DeveMarcarFalha(bool emExecucao)
    {
        var execucao = emExecucao ? Dados.EmExecucao() : Dados.NovaExecucao();

        Assert.True(execucao.PodeFalhar());
        execucao.Falhar(" Peça indisponível ");

        Assert.Equal(ExecucaoStatus.Falhou, execucao.Status);
        Assert.Equal("Peça indisponível", execucao.Motivo);
    }

    [Fact(DisplayName = "Falhar deve exigir motivo")]
    [Trait("Execução", "Domain")]
    public void Falhar_DeveExigirMotivo() =>
        Assert.Throws<DomainException>(() => Dados.NovaExecucao().Falhar(""));

    [Fact(DisplayName = "Falhar deve ser rejeitado em execuções encerradas")]
    [Trait("Execução", "Domain")]
    public void Falhar_DeveSerRejeitadoQuandoEncerrada()
    {
        Assert.False(Dados.Concluida().PodeFalhar());
        Assert.Throws<DomainException>(() => Dados.Concluida().Falhar("x"));
        Assert.Throws<DomainException>(() => Dados.Falhou().Falhar("x"));
        Assert.Throws<DomainException>(() => Dados.Removida().Falhar("x"));
    }

    [Theory(DisplayName = "Remover deve funcionar para pendente e em execução")]
    [Trait("Execução", "Domain")]
    [InlineData(false)]
    [InlineData(true)]
    public void Remover_DeveRemover(bool emExecucao)
    {
        var execucao = emExecucao ? Dados.EmExecucao() : Dados.NovaExecucao();

        Assert.True(execucao.PodeSerRemovida());
        execucao.Remover("Cliente cancelou");

        Assert.Equal(ExecucaoStatus.Removida, execucao.Status);
        Assert.Equal("Cliente cancelou", execucao.Motivo);
    }

    [Fact(DisplayName = "Remover deve exigir motivo")]
    [Trait("Execução", "Domain")]
    public void Remover_DeveExigirMotivo() =>
        Assert.Throws<DomainException>(() => Dados.NovaExecucao().Remover(" "));

    [Fact(DisplayName = "Remover deve ser rejeitado em execuções encerradas")]
    [Trait("Execução", "Domain")]
    public void Remover_DeveSerRejeitadoQuandoEncerrada()
    {
        Assert.False(Dados.Concluida().PodeSerRemovida());
        Assert.Throws<DomainException>(() => Dados.Concluida().Remover("x"));
        Assert.Throws<DomainException>(() => Dados.Falhou().Remover("x"));
        Assert.Throws<DomainException>(() => Dados.Removida().Remover("x"));
    }

    [Fact(DisplayName = "Restaurar deve preservar todos os campos e AtualizarVersao deve alterar a versão")]
    [Trait("Execução", "Domain")]
    public void Restaurar_DevePreservarCampos()
    {
        var id = Guid.NewGuid();
        var item = new ItemExecucao(Guid.NewGuid(), "Filtro", 3);

        var execucao = Execucao.Restaurar(
            id, ExecucaoStatus.EmExecucao, "Revisão", 300m, [item], Dados.Agora,
            Dados.Agora.AddMinutes(1), null, "Carlos", null, "corr", 4);

        Assert.Equal(id, execucao.OrdemServicoId);
        Assert.Equal(ExecucaoStatus.EmExecucao, execucao.Status);
        Assert.Equal("Carlos", execucao.Mecanico);
        Assert.Equal(item, execucao.Itens.Single());
        Assert.Equal(4, execucao.Versao);

        execucao.AtualizarVersao(5);

        Assert.Equal(5, execucao.Versao);
    }
}
