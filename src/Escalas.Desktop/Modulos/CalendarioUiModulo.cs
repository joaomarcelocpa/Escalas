using Escalas.Desktop.Navegacao;
using Escalas.Desktop.Servicos;
using Escalas.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Desktop.Modulos;

public static class CalendarioUiModulo
{
    /// <summary>Registra ViewModels do módulo e acrescenta suas entradas ao menu.</summary>
    public static void Registrar(IServiceCollection services, List<ItemNavegacao> menu)
    {
        services.AddSingleton<IDialogoConfirmacao, DialogoConfirmacao>();
        services.AddSingleton<IDialogosHorario, DialogosHorario>();
        services.AddTransient<CalendarioViewModel>();
        services.AddTransient<EscalaSemanalViewModel>();
        menu.Add(new ItemNavegacao("Calendário", typeof(CalendarioViewModel), Icones.Calendario));
        menu.Add(new ItemNavegacao("Escala semanal", typeof(EscalaSemanalViewModel), Icones.EscalaSemanal));
    }
}
