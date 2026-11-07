namespace wrench.execucao.application;

public enum TipoResultado
{
    Sucesso,
    Duplicado,
    NaoEncontrado,
    Conflito,
    Validacao
}

public sealed record ResultadoOperacao(TipoResultado Tipo, string? Mensagem = null)
{
    public bool Sucesso => Tipo is TipoResultado.Sucesso or TipoResultado.Duplicado;

    public static ResultadoOperacao Ok(string? mensagem = null) => new(TipoResultado.Sucesso, mensagem);
    public static ResultadoOperacao Duplicado(string? mensagem = null) => new(TipoResultado.Duplicado, mensagem);
    public static ResultadoOperacao NaoEncontrado(string mensagem) => new(TipoResultado.NaoEncontrado, mensagem);
    public static ResultadoOperacao Conflito(string mensagem) => new(TipoResultado.Conflito, mensagem);
    public static ResultadoOperacao Validacao(string mensagem) => new(TipoResultado.Validacao, mensagem);
}
