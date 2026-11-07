using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace wrench.execucao.api.Configuration;

public static class SerilogConfiguration
{
    /// <summary>
    /// Logs JSON no console e, se OTEL_EXPORTER_OTLP_ENDPOINT estiver definida, também via OTLP.
    /// </summary>
    public static void ConfigureSerilog(this WebApplicationBuilder builder)
    {
        Serilog.Debugging.SelfLog.Enable(msg => Console.Error.WriteLine($"[Serilog SelfLog] {msg}"));

        var serviceName = builder.Configuration["Observability:ServiceName"] ?? ObservabilityConfiguration.DefaultServiceName;
        var environmentName = builder.Environment.EnvironmentName;

        builder.Host.UseSerilog((_, _, loggerConfiguration) =>
        {
            loggerConfiguration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithSpan()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("service.name", serviceName)
                .Enrich.WithProperty("deployment.environment", environmentName)
                .WriteTo.Console(new RenderedCompactJsonFormatter());

            var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
            if (string.IsNullOrWhiteSpace(otlpEndpoint)) return;

            var protocol = ResolveProtocol();

            loggerConfiguration.WriteTo.OpenTelemetry(options =>
            {
                // Este sink não completa o path do sinal (/v1/logs) sozinho no protocolo HTTP.
                options.Endpoint = protocol == OtlpProtocol.HttpProtobuf
                    ? BuildHttpLogsEndpoint(otlpEndpoint)
                    : otlpEndpoint;
                options.Protocol = protocol;
                options.Headers = ParseHeaders(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS"));
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = serviceName,
                    ["deployment.environment"] = environmentName
                };
            });
        });
    }

    internal static string BuildHttpLogsEndpoint(string baseEndpoint)
    {
        var trimmed = baseEndpoint.TrimEnd('/');
        return trimmed.EndsWith("/v1/logs", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/v1/logs";
    }

    internal static OtlpProtocol ResolveProtocol()
    {
        var protocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");
        return string.Equals(protocol, "grpc", StringComparison.OrdinalIgnoreCase)
            ? OtlpProtocol.Grpc
            : OtlpProtocol.HttpProtobuf;
    }

    internal static Dictionary<string, string> ParseHeaders(string? rawHeaders)
    {
        var headers = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(rawHeaders))
            return headers;

        foreach (var pair in rawHeaders.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
                headers[parts[0].Trim()] = Uri.UnescapeDataString(parts[1].Trim());
        }

        return headers;
    }
}
