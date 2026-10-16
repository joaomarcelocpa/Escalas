using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Infrastructure.Modulos;

public static class FeriadosModulo
{
    /// <summary>Registra os serviços do módulo Feriados (repositórios/serviços atrás de interfaces da Application).</summary>
    public static IServiceCollection AdicionarFeriados(this IServiceCollection services)
    {
        return services;
    }
}
