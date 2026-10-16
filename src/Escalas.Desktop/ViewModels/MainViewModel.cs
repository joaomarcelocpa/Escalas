using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Nucleo;
using Escalas.Desktop.Navegacao;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IServiceProvider? _sp;

    public IReadOnlyList<ItemNavegacao> Menu { get; }

    [ObservableProperty]
    public partial string Titulo { get; set; } = "Escalas";

    [ObservableProperty]
    public partial string Subtitulo { get; set; } = "Controle de escalas de trabalho";

    [ObservableProperty]
    public partial ViewModelBase? PaginaAtual { get; set; }

    [ObservableProperty]
    public partial string? StatusBackup { get; set; }

    /// <summary>Construtor para o previewer.</summary>
    public MainViewModel() => Menu = [];

    public MainViewModel(IServiceProvider sp, IReadOnlyList<ItemNavegacao> menu)
    {
        _sp = sp;
        Menu = menu;
        if (menu.Count > 0) Navegar(menu[0]);
    }

    public void ExportarBackup(string destino)
    {
        try
        {
            _sp!.GetRequiredService<IBackupService>().Exportar(destino);
            StatusBackup = "Backup exportado.";
        }
        catch (Exception ex)
        {
            StatusBackup = $"Falha no backup: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Navegar(ItemNavegacao item) =>
        PaginaAtual = (ViewModelBase)_sp!.GetService(item.ViewModel)!;
}
