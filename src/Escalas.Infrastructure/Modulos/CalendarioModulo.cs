using Escalas.Application.Calendario;
using Escalas.Infrastructure.Calendario;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Infrastructure.Modulos;

public static class CalendarioModulo
{
    /// <summary>Registra os serviços do módulo Calendario.</summary>
    public static IServiceCollection AdicionarCalendario(this IServiceCollection services)
    {
        services.AddTransient<IEscalaService, EscalaService>();
        return services;
    }
}
