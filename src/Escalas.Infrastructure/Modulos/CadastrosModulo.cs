using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Infrastructure.Modulos;

public static class CadastrosModulo
{
    /// <summary>Registra os serviços do módulo Cadastros (repositórios/serviços atrás de interfaces da Application).</summary>
    public static IServiceCollection AdicionarCadastros(this IServiceCollection services)
    {
        return services;
    }
}
