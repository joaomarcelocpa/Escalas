using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Nucleo;
using Escalas.Desktop.Navegacao;
using Escalas.Desktop.Servicos;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IServiceProvider? _sp;
    private readonly IPreferenciasUi? _preferencias;

    public IReadOnlyList<ItemNavegacao> Menu { get; }

    [ObservableProperty]
    public partial string Titulo { get; set; } = "Escalas";

    [ObservableProperty]
    public partial string Subtitulo { get; set; } = "Controle de escalas de trabalho";

    [ObservableProperty]
    public partial ViewModelBase? PaginaAtual { get; set; }

    [ObservableProperty]
    public partial string? StatusBackup { get; set; }

    /// <summary>Barra lateral só com ícones, para caber em janelas menores. Lembrado entre aberturas.</summary>
    [ObservableProperty]
    public partial bool SidebarRecolhida { get; set; }

    private static readonly Geometry GeometriaExpandir = Geometry.Parse(Icones.Expandir);
    private static readonly Geometry GeometriaRecolher = Geometry.Parse(Icones.Recolher);

    public Geometry IconeAlternarSidebar => SidebarRecolhida ? GeometriaExpandir : GeometriaRecolher;
    public string DicaAlternarSidebar => SidebarRecolhida ? "Expandir menu" : "Recolher menu";
    public Geometry IconeBackup { get; } = Geometry.Parse(Icones.Backup);

    /// <summary>Construtor para o previewer.</summary>
    public MainViewModel() => Menu = [];

    public MainViewModel(IServiceProvider sp, IReadOnlyList<ItemNavegacao> menu, IPreferenciasUi preferencias)
    {
        _sp = sp;
        _preferencias = preferencias;
        Menu = menu;
        SidebarRecolhida = preferencias.SidebarRecolhida;
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

    partial void OnSidebarRecolhidaChanged(bool value)
    {
        if (_preferencias is not null) _preferencias.SidebarRecolhida = value;
        OnPropertyChanged(nameof(IconeAlternarSidebar));
        OnPropertyChanged(nameof(DicaAlternarSidebar));
    }

    [RelayCommand]
    private void AlternarSidebar() => SidebarRecolhida = !SidebarRecolhida;

    [RelayCommand]
    private void Navegar(ItemNavegacao item) =>
        PaginaAtual = (ViewModelBase)_sp!.GetService(item.ViewModel)!;
}
