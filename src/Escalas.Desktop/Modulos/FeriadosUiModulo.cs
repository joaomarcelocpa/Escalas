using Escalas.Desktop.Navegacao;
using Escalas.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Desktop.Modulos;

public static class FeriadosUiModulo
{
    /// <summary>Registra ViewModels do módulo e acrescenta suas entradas ao menu.</summary>
    public static void Registrar(IServiceCollection services, List<ItemNavegacao> menu)
    {
        services.AddTransient<FeriadosViewModel>();
        menu.Add(new ItemNavegacao("Feriados", typeof(FeriadosViewModel)));
    }
}
