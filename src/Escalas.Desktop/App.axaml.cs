using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Escalas.Application.Nucleo;
using Escalas.Desktop.Modulos;
using Escalas.Desktop.Navegacao;
using Escalas.Desktop.Servicos;
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
        IconeDock.Aplicar();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            var menu = new List<ItemNavegacao>();
            services.AdicionarNucleo();
            services.AdicionarCadastros().AdicionarFeriados().AdicionarCalendario();
            CalendarioUiModulo.Registrar(services, menu);
            CadastrosUiModulo.Registrar(services, menu);
            services.AddSingleton<IReadOnlyList<ItemNavegacao>>(menu);
            services.AddSingleton<IPreferenciasUi, PreferenciasUi>();
            services.AddSingleton<MainViewModel>();
            var sp = services.BuildServiceProvider();

            // Startup: verifica/migra o banco e abre direto a janela principal (sem login: uso local no PC da empresa).
            // Roda fora do contexto da UI para não travar o await; nenhuma janela existe ainda.
            var inicializacao = Task.Run(() => sp.GetRequiredService<IInicializadorBanco>().InicializarAsync()).GetAwaiter().GetResult();
            var bancoPronto = inicializacao.Situacao == SituacaoBanco.Pronto;

            desktop.MainWindow = bancoPronto
                ? new MainWindow { DataContext = sp.GetRequiredService<MainViewModel>() }
                : new AvisoBancoWindow(inicializacao.Mensagem ?? "Não foi possível abrir o banco de dados.");

            // Backup automático ao fechar o app (só se o banco chegou a ser aberto).
            desktop.Exit += (_, _) =>
            {
                if (bancoPronto) sp.GetRequiredService<IBackupService>().BackupAutomatico();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}