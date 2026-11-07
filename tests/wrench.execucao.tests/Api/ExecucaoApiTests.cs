using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using wrench.execucao.api.Models;
using wrench.execucao.domain;
using wrench.execucao.tests.Support;

namespace wrench.execucao.tests.Api;

public class ExecucaoApiTests : IClassFixture<ExecucaoApiFactory>
{
    private const string Base = "/api/v1/execucoes";

    private readonly ExecucaoApiFactory _fabrica;

    public ExecucaoApiTests(ExecucaoApiFactory fabrica)
    {
        _fabrica = fabrica;
        _fabrica.Repositorio.Itens.Clear();
        _fabrica.Repositorio.Outbox.Clear();
        _fabrica.Repositorio.ForcarResultado = null;
    }

    private Execucao Semear(Execucao execucao)
    {
        _fabrica.Repositorio.Itens[execucao.OrdemServicoId] = execucao;
        return execucao;
    }

    // ---- Autenticação e autorização

    [Fact(DisplayName = "Requisição sem token deve retornar 401")]
    [Trait("Execução", "Api")]
    public async Task SemToken_DeveRetornar401()
    {
        var resposta = await _fabrica.CreateClient().GetAsync(Base);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact(DisplayName = "Perfil Cliente não deve acessar a fila de execução")]
    [Trait("Execução", "Api")]
    public async Task PerfilCliente_DeveRetornar403()
    {
        var resposta = await _fabrica.CriarClienteAutenticado("Cliente").GetAsync(Base);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    // ---- Consulta

    [Fact(DisplayName = "Listar deve devolver a fila pendente por padrão")]
    [Trait("Execução", "Api")]
    public async Task Listar_DeveDevolverPendentesPorPadrao()
    {
        var pendente = Semear(Dados.NovaExecucao());
        Semear(Dados.EmExecucao());

        var resposta = await _fabrica.CriarClienteAutenticado("Funcionario").GetAsync(Base);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var fila = await resposta.Content.ReadFromJsonAsync<List<ExecucaoResponse>>();
        var item = Assert.Single(fila!);
        Assert.Equal(pendente.OrdemServicoId, item.OrdemServicoId);
        Assert.Equal("Pendente", item.Status);
        Assert.Single(item.Itens);
    }

    [Fact(DisplayName = "Listar deve aceitar o filtro de status sem diferenciar maiúsculas")]
    [Trait("Execução", "Api")]
    public async Task Listar_DeveFiltrarPorStatus()
    {
        Semear(Dados.NovaExecucao());
        var emExecucao = Semear(Dados.EmExecucao());

        var resposta = await _fabrica.CriarClienteAutenticado("Admin").GetAsync($"{Base}?status=emexecucao&limite=5");

        var fila = await resposta.Content.ReadFromJsonAsync<List<ExecucaoResponse>>();
        Assert.Equal(emExecucao.OrdemServicoId, Assert.Single(fila!).OrdemServicoId);
    }

    [Theory(DisplayName = "Listar deve rejeitar status inválido")]
    [Trait("Execução", "Api")]
    [InlineData("Inexistente")]
    [InlineData("99")]
    public async Task Listar_DeveRejeitarStatusInvalido(string status)
    {
        var resposta = await _fabrica.CriarClienteAutenticado("Funcionario").GetAsync($"{Base}?status={status}");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact(DisplayName = "Obter deve devolver a execução ou 404")]
    [Trait("Execução", "Api")]
    public async Task Obter_DeveDevolverExecucaoOu404()
    {
        var execucao = Semear(Dados.NovaExecucao());
        var cliente = _fabrica.CriarClienteAutenticado("Funcionario");

        var encontrada = await cliente.GetAsync($"{Base}/{execucao.OrdemServicoId}");
        var ausente = await cliente.GetAsync($"{Base}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, encontrada.StatusCode);
        var corpo = await encontrada.Content.ReadFromJsonAsync<ExecucaoResponse>();
        Assert.Equal(execucao.OrdemServicoId, corpo!.OrdemServicoId);
        Assert.Equal("corr-1", corpo.CorrelationId);
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
    }

    // ---- Ações

    [Fact(DisplayName = "Assumir deve usar o nome do usuário autenticado quando o corpo não informa o mecânico")]
    [Trait("Execução", "Api")]
    public async Task Assumir_DeveUsarUsuarioAutenticado()
    {
        var execucao = Semear(Dados.NovaExecucao());

        var resposta = await _fabrica.CriarClienteAutenticado("Funcionario", "ana.mecanica")
            .PostAsync($"{Base}/{execucao.OrdemServicoId}/assumir", content: null);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(ExecucaoStatus.EmExecucao, execucao.Status);
        Assert.Equal("ana.mecanica", execucao.Mecanico);
    }

    [Fact(DisplayName = "Assumir deve preferir o mecânico informado no corpo")]
    [Trait("Execução", "Api")]
    public async Task Assumir_DevePreferirMecanicoDoCorpo()
    {
        var execucao = Semear(Dados.NovaExecucao());

        var resposta = await _fabrica.CriarClienteAutenticado("Funcionario")
            .PostAsJsonAsync($"{Base}/{execucao.OrdemServicoId}/assumir", new AssumirExecucaoRequest("Carlos"));

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal("Carlos", execucao.Mecanico);
    }

    [Fact(DisplayName = "Assumir execução inexistente deve retornar 404 e já assumida 409")]
    [Trait("Execução", "Api")]
    public async Task Assumir_DeveRetornar404E409()
    {
        var jaAssumida = Semear(Dados.EmExecucao());
        var cliente = _fabrica.CriarClienteAutenticado("Funcionario");

        var ausente = await cliente.PostAsync($"{Base}/{Guid.NewGuid()}/assumir", content: null);
        var conflito = await cliente.PostAsync($"{Base}/{jaAssumida.OrdemServicoId}/assumir", content: null);

        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
    }

    [Fact(DisplayName = "Concluir deve finalizar a execução e registrar ExecucaoConcluida no outbox")]
    [Trait("Execução", "Api")]
    public async Task Concluir_DeveFinalizarEGravarOutbox()
    {
        var execucao = Semear(Dados.EmExecucao());

        var resposta = await _fabrica.CriarClienteAutenticado("Funcionario")
            .PostAsync($"{Base}/{execucao.OrdemServicoId}/concluir", content: null);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(ExecucaoStatus.Concluida, execucao.Status);
        Assert.Equal("ExecucaoConcluida", Assert.Single(_fabrica.Repositorio.Outbox).Tipo);
    }

    [Fact(DisplayName = "Concluir execução não assumida deve retornar 409")]
    [Trait("Execução", "Api")]
    public async Task Concluir_NaoAssumida_DeveRetornar409()
    {
        var execucao = Semear(Dados.NovaExecucao());

        var resposta = await _fabrica.CriarClienteAutenticado("Funcionario")
            .PostAsync($"{Base}/{execucao.OrdemServicoId}/concluir", content: null);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Empty(_fabrica.Repositorio.Outbox);
    }

    [Fact(DisplayName = "Falhar deve responder 404 quando a falha forçada está desligada")]
    [Trait("Execução", "Api")]
    public async Task Falhar_Desligado_DeveRetornar404()
    {
        var execucao = Semear(Dados.NovaExecucao());

        var resposta = await _fabrica.CriarClienteAutenticado("Admin")
            .PostAsJsonAsync($"{Base}/{execucao.OrdemServicoId}/falhar", new FalharExecucaoRequest("Teste"));

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal(ExecucaoStatus.Pendente, execucao.Status);
    }

    [Fact(DisplayName = "Retorno 400 de validação deve ser devolvido como ProblemDetails")]
    [Trait("Execução", "Api")]
    public async Task Assumir_SemMecanicoNemUsuario_DeveRetornar400()
    {
        var execucao = Semear(Dados.NovaExecucao());
        var cliente = _fabrica.CriarClienteAutenticado("Funcionario");

        var resposta = await cliente.PostAsJsonAsync($"{Base}/{execucao.OrdemServicoId}/assumir", new AssumirExecucaoRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        using var problema = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal(400, problema.RootElement.GetProperty("status").GetInt32());
    }

    // ---- Transversais

    [Fact(DisplayName = "Resposta deve ecoar o X-Correlation-ID recebido e gerar um quando ausente")]
    [Trait("Execução", "Api")]
    public async Task Resposta_DeveTerCorrelationId()
    {
        var cliente = _fabrica.CriarClienteAutenticado("Funcionario");
        cliente.DefaultRequestHeaders.Add("X-Correlation-ID", "corr-api");
        var comHeader = await cliente.GetAsync(Base);

        var semHeader = await _fabrica.CriarClienteAutenticado("Funcionario").GetAsync(Base);

        Assert.Equal("corr-api", comHeader.Headers.GetValues("X-Correlation-ID").Single());
        Assert.True(Guid.TryParse(semHeader.Headers.GetValues("X-Correlation-ID").Single(), out _));
    }

    [Fact(DisplayName = "Health live deve responder 200 e ready deve consultar o DynamoDB")]
    [Trait("Execução", "Api")]
    public async Task Health_DeveResponder()
    {
        var cliente = _fabrica.CreateClient();

        var live = await cliente.GetAsync("/health");
        var ready = await cliente.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact(DisplayName = "Raiz deve redirecionar para a documentação e o OpenAPI deve ser publicado")]
    [Trait("Execução", "Api")]
    public async Task Documentacao_DeveEstarDisponivel()
    {
        var cliente = _fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var raiz = await cliente.GetAsync("/");
        var openapi = await cliente.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.MovedPermanently, raiz.StatusCode);
        Assert.Equal("/docs-ui", raiz.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, openapi.StatusCode);
        var documento = await openapi.Content.ReadAsStringAsync();
        Assert.Contains("/api/v{version}/execucoes", documento);
        Assert.Contains("Bearer", documento);
    }
}

public class ExecucaoApiFalhaForcadaTests : IClassFixture<ExecucaoApiFactoryFalhaForcada>
{
    private readonly ExecucaoApiFactoryFalhaForcada _fabrica;

    public ExecucaoApiFalhaForcadaTests(ExecucaoApiFactoryFalhaForcada fabrica)
    {
        _fabrica = fabrica;
        _fabrica.Repositorio.Itens.Clear();
        _fabrica.Repositorio.Outbox.Clear();
    }

    [Fact(DisplayName = "Falhar habilitado deve marcar a falha e registrar ExecucaoFalhou no outbox")]
    [Trait("Execução", "Api")]
    public async Task Falhar_Habilitado_DeveMarcarFalha()
    {
        var execucao = Dados.EmExecucao();
        _fabrica.Repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resposta = await _fabrica.CriarClienteAutenticado("Admin")
            .PostAsJsonAsync($"/api/v1/execucoes/{execucao.OrdemServicoId}/falhar", new FalharExecucaoRequest("Peça indisponível"));

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(ExecucaoStatus.Falhou, execucao.Status);
        Assert.Equal("ExecucaoFalhou", Assert.Single(_fabrica.Repositorio.Outbox).Tipo);
    }

    [Fact(DisplayName = "Falhar habilitado deve exigir perfil Admin")]
    [Trait("Execução", "Api")]
    public async Task Falhar_Habilitado_DeveExigirAdmin()
    {
        var execucao = Dados.NovaExecucao();
        _fabrica.Repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resposta = await _fabrica.CriarClienteAutenticado("Funcionario")
            .PostAsJsonAsync($"/api/v1/execucoes/{execucao.OrdemServicoId}/falhar", new FalharExecucaoRequest("Teste"));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact(DisplayName = "Falhar sem motivo deve retornar 400")]
    [Trait("Execução", "Api")]
    public async Task Falhar_SemMotivo_DeveRetornar400()
    {
        var execucao = Dados.NovaExecucao();
        _fabrica.Repositorio.Itens[execucao.OrdemServicoId] = execucao;

        var resposta = await _fabrica.CriarClienteAutenticado("Admin")
            .PostAsJsonAsync($"/api/v1/execucoes/{execucao.OrdemServicoId}/falhar", new FalharExecucaoRequest(" "));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
