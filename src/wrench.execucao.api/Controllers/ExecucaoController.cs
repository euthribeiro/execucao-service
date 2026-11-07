using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using wrench.execucao.api.Models;
using wrench.execucao.api.Options;
using wrench.execucao.application;
using wrench.execucao.domain;

namespace wrench.execucao.api.Controllers;

/// <summary>
/// Fila de execução das ordens de serviço aprovadas.
/// </summary>
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/execucoes")]
[ApiController]
[Authorize(Roles = "Admin,Funcionario")]
public class ExecucaoController(ExecucaoService servico, IOptions<ExecucaoOptions> opcoes) : ControllerBase
{
    /// <summary>
    /// Lista a fila de execução por status, da mais antiga para a mais recente.
    /// </summary>
    /// <param name="status">Status desejado (padrão: Pendente).</param>
    /// <param name="limite">Quantidade máxima de itens (1 a 100, padrão 50).</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ExecucaoResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string status = nameof(ExecucaoStatus.Pendente),
        [FromQuery] int limite = 50,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ExecucaoStatus>(status, ignoreCase: true, out var statusFila) ||
            !Enum.IsDefined(statusFila))
        {
            var validos = string.Join(", ", Enum.GetNames<ExecucaoStatus>());
            return Problem($"Status inválido. Valores aceitos: {validos}.", statusCode: StatusCodes.Status400BadRequest);
        }

        var execucoes = await servico.ListarFilaAsync(statusFila, limite, cancellationToken);

        return Ok(execucoes.Select(ExecucaoResponse.De).ToList());
    }

    /// <summary>
    /// Consulta a execução de uma ordem de serviço.
    /// </summary>
    [HttpGet("{ordemServicoId:guid}")]
    [ProducesResponseType<ExecucaoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter([FromRoute] Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var execucao = await servico.ObterAsync(ordemServicoId, cancellationToken);

        return execucao is null
            ? Problem("Execução não encontrada.", statusCode: StatusCodes.Status404NotFound)
            : Ok(ExecucaoResponse.De(execucao));
    }

    /// <summary>
    /// O mecânico assume uma execução pendente.
    /// </summary>
    [HttpPost("{ordemServicoId:guid}/assumir")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Assumir(
        [FromRoute] Guid ordemServicoId,
        [FromBody] AssumirExecucaoRequest? requisicao,
        CancellationToken cancellationToken)
    {
        var mecanico = requisicao?.Mecanico
            ?? User.Identity?.Name
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue(ClaimTypes.Email);

        var resultado = await servico.AssumirAsync(ordemServicoId, mecanico ?? string.Empty, cancellationToken);

        return Responder(resultado);
    }

    /// <summary>
    /// Conclui a execução em andamento e notifica a ordem de serviço (ExecucaoConcluida).
    /// </summary>
    [HttpPost("{ordemServicoId:guid}/concluir")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Concluir([FromRoute] Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var resultado = await servico.ConcluirAsync(ordemServicoId, cancellationToken);

        return Responder(resultado);
    }

    /// <summary>
    /// Força a falha da execução (ExecucaoFalhou) para demonstrar a compensação da Saga.
    /// Disponível apenas quando Execucao:PermitirFalhaForcada estiver habilitado.
    /// </summary>
    [HttpPost("{ordemServicoId:guid}/falhar")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Falhar(
        [FromRoute] Guid ordemServicoId,
        [FromBody] FalharExecucaoRequest requisicao,
        CancellationToken cancellationToken)
    {
        if (!opcoes.Value.PermitirFalhaForcada)
            return NotFound();

        var resultado = await servico.FalharAsync(ordemServicoId, requisicao.Motivo, cancellationToken);

        return Responder(resultado);
    }

    private IActionResult Responder(ResultadoOperacao resultado) => resultado.Tipo switch
    {
        TipoResultado.Sucesso or TipoResultado.Duplicado => NoContent(),
        TipoResultado.NaoEncontrado => Problem(resultado.Mensagem, statusCode: StatusCodes.Status404NotFound),
        TipoResultado.Conflito => Problem(resultado.Mensagem, statusCode: StatusCodes.Status409Conflict),
        _ => Problem(resultado.Mensagem, statusCode: StatusCodes.Status400BadRequest)
    };
}
