using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Infrastructure.Modulos;

public static class CalendarioModulo
{
    /// <summary>Registra os serviços do módulo Calendario (repositórios/serviços atrás de interfaces da Application).</summary>
    public static IServiceCollection AdicionarCalendario(this IServiceCollection services)
    {
        return services;
    }
}
