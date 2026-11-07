using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using wrench.execucao.api.Configuration;
using wrench.execucao.api.Middlewares;
using wrench.execucao.api.Options;
using wrench.execucao.application;
using wrench.execucao.infra;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSerilog();

builder.Services.AddProblemDetails();
builder.Services.AddObservability(builder.Configuration, builder.Environment);

builder.Services.Configure<ExecucaoOptions>(builder.Configuration.GetSection(ExecucaoOptions.Secao));
builder.Services.ConfigureAuthentication(builder.Configuration);

builder.Services
    .AddExecucaoApplication()
    .AddExecucaoInfra(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DynamoHealthCheck>("dynamodb", tags: ["ready"]);

builder.Services.ConfigureOpenApi();
builder.Services.AddControllers();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1.0);
    options.ReportApiVersions = true;
    options.AssumeDefaultVersionWhenUnspecified = true;
});

var app = builder.Build();

app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference("docs-ui", options => options.Title = "Wrench Execução");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => Results.Redirect("/docs-ui", permanent: true));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    AllowCachingResponses = false,
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program;
