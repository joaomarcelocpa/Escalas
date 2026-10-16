using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Escalas.Application.Nucleo;
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
            var services = new ServiceCollection();
            var menu = new List<ItemNavegacao>();
            services.AdicionarNucleo();
            services.AdicionarCadastros().AdicionarFeriados().AdicionarCalendario();
            CadastrosUiModulo.Registrar(services, menu);
            FeriadosUiModulo.Registrar(services, menu);
            CalendarioUiModulo.Registrar(services, menu);
            services.AddSingleton<IReadOnlyList<ItemNavegacao>>(menu);
            services.AddSingleton<MainViewModel>();
            var sp = services.BuildServiceProvider();

            // Startup: verifica/migra o banco e pede login; o shell só abre depois do login.
            var login = new LoginViewModel(sp.GetRequiredService<IInicializadorBanco>(), () => sp.GetRequiredService<IAutenticacaoService>());
            var janelaLogin = new LoginWindow { DataContext = login };
            desktop.MainWindow = janelaLogin;
            janelaLogin.Opened += async (_, _) => await login.CarregarAsync();
            login.LoginConcluido += _ =>
            {
                var principal = new MainWindow { DataContext = sp.GetRequiredService<MainViewModel>() };
                desktop.MainWindow = principal;
                principal.Show();
                janelaLogin.Close();
            };

            // Backup automático ao fechar o app (só se o banco chegou a ser aberto).
            desktop.Exit += (_, _) =>
            {
                if (login.BancoPronto) sp.GetRequiredService<IBackupService>().BackupAutomatico();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}