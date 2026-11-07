using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace wrench.execucao.application;

public static class DependencyInjection
{
    public static IServiceCollection AddExecucaoApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ExecucaoService>();
        return services;
    }
}
