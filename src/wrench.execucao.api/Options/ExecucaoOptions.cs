namespace wrench.execucao.api.Options;

public class ExecucaoOptions
{
    public const string Secao = "Execucao";

    /// <summary>
    /// Habilita o endpoint que força a falha de uma execução (usado para demonstrar a
    /// compensação da Saga). Deve ficar desligado em produção.
    /// </summary>
    public bool PermitirFalhaForcada { get; set; }
}
