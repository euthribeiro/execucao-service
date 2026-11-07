using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Moq;
using wrench.execucao.application;
using wrench.execucao.domain;
using wrench.execucao.infra;
using wrench.execucao.tests.Support;

namespace wrench.execucao.tests.Infra;

public class DynamoExecucaoRepositoryTests
{
    private static readonly DateTimeOffset Agora = new(Dados.Agora);

    private readonly Mock<IAmazonDynamoDB> _dynamo = new();
    private readonly DynamoOptions _opcoes = new();
    private readonly DynamoExecucaoRepository _repositorio;
    private TransactWriteItemsRequest? _transacao;

    public DynamoExecucaoRepositoryTests()
    {
        _repositorio = new DynamoExecucaoRepository(_dynamo.Object, Options.Create(_opcoes), new RelogioFixo(Agora));

        _dynamo
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => _transacao = r)
            .ReturnsAsync(new TransactWriteItemsResponse());
    }

    private static TransactionCanceledException Cancelada(params string[] codigos) =>
        new("cancelada") { CancellationReasons = [.. codigos.Select(c => new CancellationReason { Code = c })] };

    // ---- Leitura

    [Fact(DisplayName = "Obter deve ler com consistência forte e mapear o item")]
    [Trait("Execução", "Infra")]
    public async Task Obter_DeveMapearItem()
    {
        var execucao = Dados.EmExecucao();
        GetItemRequest? capturada = null;
        _dynamo
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetItemRequest, CancellationToken>((r, _) => capturada = r)
            .ReturnsAsync(new GetItemResponse { Item = ExecucaoMapper.ParaItem(execucao, 3) });

        var resultado = await _repositorio.ObterAsync(execucao.OrdemServicoId, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(execucao.OrdemServicoId, resultado.OrdemServicoId);
        Assert.Equal(ExecucaoStatus.EmExecucao, resultado.Status);
        Assert.Equal(3, resultado.Versao);
        Assert.Equal(_opcoes.Execucao, capturada!.TableName);
        Assert.True(capturada.ConsistentRead);
        Assert.Equal(execucao.OrdemServicoId.ToString(), capturada.Key["ordemServicoId"].S);
    }

    [Fact(DisplayName = "Obter deve devolver nulo quando o item não existe")]
    [Trait("Execução", "Infra")]
    public async Task Obter_DeveDevolverNulo()
    {
        _dynamo
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = [] });

        Assert.Null(await _repositorio.ObterAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact(DisplayName = "Listar por status deve consultar o índice em ordem crescente")]
    [Trait("Execução", "Infra")]
    public async Task ListarPorStatus_DeveConsultarIndice()
    {
        var execucao = Dados.NovaExecucao();
        QueryRequest? capturada = null;
        _dynamo
            .Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<QueryRequest, CancellationToken>((r, _) => capturada = r)
            .ReturnsAsync(new QueryResponse { Items = [ExecucaoMapper.ParaItem(execucao, 0)] });

        var fila = await _repositorio.ListarPorStatusAsync(ExecucaoStatus.Pendente, 20, CancellationToken.None);

        Assert.Equal(execucao.OrdemServicoId, Assert.Single(fila).OrdemServicoId);
        Assert.Equal(Indices.ExecucaoPorStatus, capturada!.IndexName);
        Assert.Equal("Pendente", capturada.ExpressionAttributeValues[":s"].S);
        Assert.True(capturada.ScanIndexForward);
        Assert.Equal(20, capturada.Limit);
    }

    // ---- Escrita

    [Fact(DisplayName = "Salvar nova execução deve gravar com condição de inexistência e versão 1")]
    [Trait("Execução", "Infra")]
    public async Task Salvar_Nova_DeveGravarComCondicaoDeInexistencia()
    {
        var execucao = Dados.NovaExecucao();

        var resultado = await _repositorio.SalvarAsync(execucao, nova: true, messageId: null, [], CancellationToken.None);

        Assert.Equal(SalvarResultado.Salvo, resultado);
        Assert.Equal(1, execucao.Versao);
        var put = Assert.Single(_transacao!.TransactItems).Put;
        Assert.Equal(_opcoes.Execucao, put.TableName);
        Assert.Equal("attribute_not_exists(ordemServicoId)", put.ConditionExpression);
        Assert.Equal("1", put.Item["versao"].N);
    }

    [Fact(DisplayName = "Salvar execução existente deve exigir a versão atual e incrementá-la")]
    [Trait("Execução", "Infra")]
    public async Task Salvar_Existente_DeveExigirVersaoAtual()
    {
        var execucao = Execucao.Restaurar(
            Guid.NewGuid(), ExecucaoStatus.Pendente, "Revisão", 10m, [], Dados.Agora, null, null, null, null, null, 4);

        var resultado = await _repositorio.SalvarAsync(execucao, nova: false, messageId: null, [], CancellationToken.None);

        Assert.Equal(SalvarResultado.Salvo, resultado);
        Assert.Equal(5, execucao.Versao);
        var put = Assert.Single(_transacao!.TransactItems).Put;
        Assert.Equal("versao = :v", put.ConditionExpression);
        Assert.Equal("4", put.ExpressionAttributeValues[":v"].N);
        Assert.Equal("5", put.Item["versao"].N);
    }

    [Fact(DisplayName = "Salvar com mensagem e outbox deve gravar idempotência e eventos na mesma transação")]
    [Trait("Execução", "Infra")]
    public async Task Salvar_ComMensagemEOutbox_DeveGravarTudoAtomicamente()
    {
        var execucao = Dados.NovaExecucao();
        var outbox = new[]
        {
            OutboxMessage.Criar("A", new { x = 1 }, "corr", Dados.Agora),
            OutboxMessage.Criar("B", new { x = 2 }, null, Dados.Agora)
        };

        await _repositorio.SalvarAsync(execucao, nova: true, "msg-9", outbox, CancellationToken.None);

        var itens = _transacao!.TransactItems;
        Assert.Equal(4, itens.Count);
        Assert.Equal(_opcoes.Execucao, itens[0].Put.TableName);

        var idempotencia = itens[1].Put;
        Assert.Equal(_opcoes.Idempotencia, idempotencia.TableName);
        Assert.Equal("msg-9", idempotencia.Item["messageId"].S);
        Assert.Equal("attribute_not_exists(messageId)", idempotencia.ConditionExpression);
        var ttlEsperado = Agora.AddDays(_opcoes.IdempotenciaDias).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Assert.Equal(ttlEsperado, idempotencia.Item["ttl"].N);

        Assert.All(itens.Skip(2), i => Assert.Equal(_opcoes.Outbox, i.Put.TableName));
        Assert.Equal("Pendente", itens[2].Put.Item["status"].S);
        Assert.Equal("corr", itens[2].Put.Item["correlationId"].S);
        Assert.False(itens[3].Put.Item.ContainsKey("correlationId"));
    }

    [Fact(DisplayName = "Salvar deve traduzir falha de condição da mensagem como duplicidade")]
    [Trait("Execução", "Infra")]
    public async Task Salvar_FalhaNaIdempotencia_DeveRetornarDuplicado()
    {
        _dynamo
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Cancelada("None", "ConditionalCheckFailed"));
        var execucao = Dados.NovaExecucao();

        var resultado = await _repositorio.SalvarAsync(execucao, nova: false, "msg-1", [], CancellationToken.None);

        Assert.Equal(SalvarResultado.Duplicado, resultado);
        Assert.Equal(0, execucao.Versao);
    }

    [Fact(DisplayName = "Salvar nova execução já existente deve retornar duplicidade")]
    [Trait("Execução", "Infra")]
    public async Task Salvar_NovaJaExistente_DeveRetornarDuplicado()
    {
        _dynamo
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Cancelada("ConditionalCheckFailed"));

        var resultado = await _repositorio.SalvarAsync(Dados.NovaExecucao(), nova: true, null, [], CancellationToken.None);

        Assert.Equal(SalvarResultado.Duplicado, resultado);
    }

    [Fact(DisplayName = "Salvar execução existente com versão desatualizada deve retornar conflito")]
    [Trait("Execução", "Infra")]
    public async Task Salvar_VersaoDesatualizada_DeveRetornarConflito()
    {
        _dynamo
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Cancelada("ConditionalCheckFailed"));
        var execucao = Dados.NovaExecucao();

        var resultado = await _repositorio.SalvarAsync(execucao, nova: false, null, [], CancellationToken.None);

        Assert.Equal(SalvarResultado.ConflitoDeVersao, resultado);
        Assert.Equal(0, execucao.Versao);
    }

    [Fact(DisplayName = "Salvar deve propagar cancelamentos que não são de condição")]
    [Trait("Execução", "Infra")]
    public async Task Salvar_OutroMotivo_DevePropagar()
    {
        _dynamo
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Cancelada("ThrottlingError"));

        await Assert.ThrowsAsync<TransactionCanceledException>(() =>
            _repositorio.SalvarAsync(Dados.NovaExecucao(), nova: false, null, [], CancellationToken.None));
    }

    // ---- Mapeamento

    [Fact(DisplayName = "Mapper deve preservar todos os campos no ciclo item <-> domínio")]
    [Trait("Execução", "Infra")]
    public void Mapper_DevePreservarCampos()
    {
        var execucao = Execucao.Restaurar(
            Guid.NewGuid(), ExecucaoStatus.Falhou, "Troca de embreagem", 1234.56m,
            [new ItemExecucao(Guid.NewGuid(), "Kit embreagem", 1), new ItemExecucao(Guid.NewGuid(), "Fluido", 2)],
            Dados.Agora, Dados.Agora.AddMinutes(5), Dados.Agora.AddHours(1), "Carlos", "Peça indisponível", "corr-x", 7);

        var restaurada = ExecucaoMapper.ParaDominio(ExecucaoMapper.ParaItem(execucao, 8));

        Assert.Equal(execucao.OrdemServicoId, restaurada.OrdemServicoId);
        Assert.Equal(execucao.Status, restaurada.Status);
        Assert.Equal(1234.56m, restaurada.ValorAprovado);
        Assert.Equal(execucao.Itens, restaurada.Itens);
        Assert.Equal(execucao.EnfileiradaEm, restaurada.EnfileiradaEm);
        Assert.Equal(execucao.IniciadaEm, restaurada.IniciadaEm);
        Assert.Equal(execucao.ConcluidaEm, restaurada.ConcluidaEm);
        Assert.Equal("Carlos", restaurada.Mecanico);
        Assert.Equal("Peça indisponível", restaurada.Motivo);
        Assert.Equal("corr-x", restaurada.CorrelationId);
        Assert.Equal(8, restaurada.Versao);
    }

    [Fact(DisplayName = "Mapper deve omitir atributos opcionais ausentes")]
    [Trait("Execução", "Infra")]
    public void Mapper_DeveOmitirOpcionais()
    {
        var item = ExecucaoMapper.ParaItem(Execucao.Enfileirar(Guid.NewGuid(), "Revisão", 1m, [], null, Dados.Agora), 1);

        Assert.False(item.ContainsKey("iniciadaEm"));
        Assert.False(item.ContainsKey("concluidaEm"));
        Assert.False(item.ContainsKey("mecanico"));
        Assert.False(item.ContainsKey("motivo"));
        Assert.False(item.ContainsKey("correlationId"));

        var restaurada = ExecucaoMapper.ParaDominio(item);

        Assert.Null(restaurada.IniciadaEm);
        Assert.Null(restaurada.Mecanico);
        Assert.Empty(restaurada.Itens);
    }

    [Fact(DisplayName = "Mapper deve tolerar item sem lista de itens")]
    [Trait("Execução", "Infra")]
    public void Mapper_DeveTolerarSemListaDeItens()
    {
        var item = ExecucaoMapper.ParaItem(Dados.NovaExecucao(), 1);
        item.Remove("itens");

        Assert.Empty(ExecucaoMapper.ParaDominio(item).Itens);
    }
}
