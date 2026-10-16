using Escalas.Application.Feriados;
using Escalas.Infrastructure.Feriados;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Infrastructure.Modulos;

public static class FeriadosModulo
{
    /// <summary>Registra cliente BrasilAPI, cache anual, CRUD de feriados locais e <see cref="IFeriadoConsulta"/>.</summary>
    public static IServiceCollection AdicionarFeriados(this IServiceCollection services)
    {
        services.AddHttpClient(BrasilApiFeriadosClient.NomeClient, c =>
        {
            c.BaseAddress = new Uri("https://brasilapi.com.br/");
            c.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFeriadosApi, BrasilApiFeriadosClient>();
        services.AddSingleton<FeriadoService>();
        services.AddSingleton<IFeriadoService>(sp => sp.GetRequiredService<FeriadoService>());
        services.AddSingleton<IFeriadoConsulta>(sp => sp.GetRequiredService<FeriadoService>());
        return services;
    }
}
