using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using wrench.execucao.application;

namespace wrench.execucao.infra;

public static class DependencyInjection
{
    public static IServiceCollection AddExecucaoInfra(this IServiceCollection services, IConfiguration configuracao)
    {
        services.Configure<DynamoOptions>(configuracao.GetSection(DynamoOptions.Secao));
        services.Configure<SnsOptions>(configuracao.GetSection(SnsOptions.Secao));
        services.Configure<SqsOptions>(configuracao.GetSection(SqsOptions.Secao));
        services.Configure<OutboxOptions>(configuracao.GetSection(OutboxOptions.Secao));

        var dynamoOpcoes = configuracao.GetSection(DynamoOptions.Secao).Get<DynamoOptions>() ?? new DynamoOptions();
        var snsOpcoes = configuracao.GetSection(SnsOptions.Secao).Get<SnsOptions>() ?? new SnsOptions();
        var sqsOpcoes = configuracao.GetSection(SqsOptions.Secao).Get<SqsOptions>() ?? new SqsOptions();
        var regiao = dynamoOpcoes.Region ?? configuracao["AWS:Region"];

        services.TryAddSingleton(TimeProvider.System);

        // Em desenvolvimento (endpoint local) usa credenciais fictícias; em AWS segue a cadeia padrão (IRSA).
        services.AddSingleton<IAmazonDynamoDB>(_ =>
        {
            var config = CriarConfigDynamo(dynamoOpcoes.ServiceUrl, regiao);
            return string.IsNullOrWhiteSpace(dynamoOpcoes.ServiceUrl)
                ? new AmazonDynamoDBClient(config)
                : new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"), config);
        });

        services.AddSingleton<IAmazonSimpleNotificationService>(_ =>
        {
            var config = CriarConfigSns(snsOpcoes.ServiceUrl, regiao);
            return string.IsNullOrWhiteSpace(snsOpcoes.ServiceUrl)
                ? new AmazonSimpleNotificationServiceClient(config)
                : new AmazonSimpleNotificationServiceClient(new BasicAWSCredentials("local", "local"), config);
        });

        services.AddSingleton<IAmazonSQS>(_ =>
        {
            var config = CriarConfigSqs(sqsOpcoes.ServiceUrl, regiao);
            return string.IsNullOrWhiteSpace(sqsOpcoes.ServiceUrl)
                ? new AmazonSQSClient(config)
                : new AmazonSQSClient(new BasicAWSCredentials("local", "local"), config);
        });

        services.AddSingleton<IExecucaoRepository, DynamoExecucaoRepository>();
        services.AddSingleton<IOutboxStore, DynamoOutboxStore>();
        services.AddSingleton<IMessagePublisher, SnsMessagePublisher>();

        services.AddHostedService<DynamoTableInitializer>();
        services.AddHostedService<OutboxDispatcher>();
        services.AddHostedService<SqsComandoConsumer>();

        return services;
    }

    private static AmazonDynamoDBConfig CriarConfigDynamo(string? serviceUrl, string? regiao)
    {
        var config = new AmazonDynamoDBConfig();
        if (!string.IsNullOrWhiteSpace(serviceUrl)) config.ServiceURL = serviceUrl;
        if (!string.IsNullOrWhiteSpace(regiao)) config.RegionEndpoint = RegionEndpoint.GetBySystemName(regiao);
        else if (!string.IsNullOrWhiteSpace(serviceUrl)) config.AuthenticationRegion = "us-east-1";
        return config;
    }

    private static AmazonSimpleNotificationServiceConfig CriarConfigSns(string? serviceUrl, string? regiao)
    {
        var config = new AmazonSimpleNotificationServiceConfig();
        if (!string.IsNullOrWhiteSpace(serviceUrl)) config.ServiceURL = serviceUrl;
        if (!string.IsNullOrWhiteSpace(regiao)) config.RegionEndpoint = RegionEndpoint.GetBySystemName(regiao);
        else if (!string.IsNullOrWhiteSpace(serviceUrl)) config.AuthenticationRegion = "us-east-1";
        return config;
    }

    private static AmazonSQSConfig CriarConfigSqs(string? serviceUrl, string? regiao)
    {
        var config = new AmazonSQSConfig();
        if (!string.IsNullOrWhiteSpace(serviceUrl)) config.ServiceURL = serviceUrl;
        if (!string.IsNullOrWhiteSpace(regiao)) config.RegionEndpoint = RegionEndpoint.GetBySystemName(regiao);
        else if (!string.IsNullOrWhiteSpace(serviceUrl)) config.AuthenticationRegion = "us-east-1";
        return config;
    }
}
