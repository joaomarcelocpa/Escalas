using Escalas.Application.Cadastros;
using Escalas.Infrastructure.Cadastros;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Infrastructure.Modulos;

public static class CadastrosModulo
{
    /// <summary>Registra os serviços do módulo Cadastros (setores e funcionários; as filiais são fixas).</summary>
    public static IServiceCollection AdicionarCadastros(this IServiceCollection services)
    {
        services.AddTransient<ISetorService, SetorService>();
        services.AddTransient<IFuncionarioService, FuncionarioService>();
        return services;
    }
}
