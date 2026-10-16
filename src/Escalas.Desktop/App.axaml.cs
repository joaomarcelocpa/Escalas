using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Escalas.Desktop.Modulos;
using Escalas.Desktop.Navegacao;
using Escalas.Desktop.ViewModels;
using Escalas.Infrastructure.Modulos;
using Microsoft.Extensions.DependencyInjection;
using Escalas.Desktop.Views;

namespace Escalas.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // TODO(Fase 2): fluxo de startup (banco existe? login?) antes do shell.
            var services = new ServiceCollection();
            var menu = new List<ItemNavegacao>();
            services.AdicionarCadastros().AdicionarFeriados().AdicionarCalendario();
            CadastrosUiModulo.Registrar(services, menu);
            FeriadosUiModulo.Registrar(services, menu);
            CalendarioUiModulo.Registrar(services, menu);
            services.AddSingleton<IReadOnlyList<ItemNavegacao>>(menu);
            services.AddSingleton<MainViewModel>();
            var sp = services.BuildServiceProvider();
            desktop.MainWindow = new MainWindow { DataContext = sp.GetRequiredService<MainViewModel>() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}