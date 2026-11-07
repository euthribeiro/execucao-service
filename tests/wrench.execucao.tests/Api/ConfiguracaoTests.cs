using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Sinks.OpenTelemetry;
using wrench.execucao.api.Configuration;

namespace wrench.execucao.tests.Api;

public class ConfiguracaoTests
{
    [Theory(DisplayName = "Autenticação deve exigir segredo, emissor e audiência")]
    [Trait("Execução", "Api")]
    [InlineData(null, "emissor", "audiencia")]
    [InlineData("segredo", null, "audiencia")]
    [InlineData("segredo", "emissor", "")]
    public void Autenticacao_DeveExigirConfiguracaoCompleta(string? segredo, string? emissor, string? audiencia)
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:Secret"] = segredo,
            ["JwtOptions:Issuer"] = emissor,
            ["JwtOptions:Audience"] = audiencia
        }).Build();

        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().ConfigureAuthentication(configuracao));
    }

    [Theory(DisplayName = "Endpoint HTTP de logs deve ganhar o path /v1/logs apenas quando ausente")]
    [Trait("Execução", "Api")]
    [InlineData("https://otlp.nr-data.net:4318", "https://otlp.nr-data.net:4318/v1/logs")]
    [InlineData("https://otlp.nr-data.net:4318/", "https://otlp.nr-data.net:4318/v1/logs")]
    [InlineData("https://otlp.nr-data.net:4318/v1/logs", "https://otlp.nr-data.net:4318/v1/logs")]
    public void EndpointDeLogs_DeveCompletarOPath(string entrada, string esperado) =>
        Assert.Equal(esperado, SerilogConfiguration.BuildHttpLogsEndpoint(entrada));

    [Fact(DisplayName = "Headers OTLP devem ser lidos no formato chave=valor separado por vírgula")]
    [Trait("Execução", "Api")]
    public void Headers_DevemSerInterpretados()
    {
        var headers = SerilogConfiguration.ParseHeaders("api-key=abc%20123, outro = valor ,invalido");

        Assert.Equal("abc 123", headers["api-key"]);
        Assert.Equal("valor", headers["outro"]);
        Assert.Equal(2, headers.Count);
        Assert.Empty(SerilogConfiguration.ParseHeaders(null));
        Assert.Empty(SerilogConfiguration.ParseHeaders("  "));
    }

    [Fact(DisplayName = "Protocolo OTLP deve ser HTTP/protobuf por padrão e gRPC quando solicitado")]
    [Trait("Execução", "Api")]
    public void Protocolo_DeveSeguirAVariavelDeAmbiente()
    {
        var original = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");
        try
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", null);
            Assert.Equal(OtlpProtocol.HttpProtobuf, SerilogConfiguration.ResolveProtocol());

            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "GRPC");
            Assert.Equal(OtlpProtocol.Grpc, SerilogConfiguration.ResolveProtocol());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", original);
        }
    }
}
