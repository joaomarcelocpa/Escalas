using Escalas.Desktop.Navegacao;
using Escalas.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Desktop.Modulos;

public static class CalendarioUiModulo
{
    /// <summary>Registra ViewModels do módulo e acrescenta suas entradas ao menu.</summary>
    public static void Registrar(IServiceCollection services, List<ItemNavegacao> menu)
    {
        services.AddTransient<CalendarioViewModel>();
        menu.Add(new ItemNavegacao("Calendário", typeof(CalendarioViewModel)));
    }
}
