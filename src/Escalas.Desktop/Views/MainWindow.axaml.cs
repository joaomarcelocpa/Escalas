using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Escalas.Desktop.Servicos;
using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Túnel: o atalho de zoom funciona mesmo com o foco num campo de texto.
        AddHandler(KeyDownEvent, AtalhoZoom, RoutingStrategies.Tunnel);
    }

    private void AtalhoZoom(object? sender, KeyEventArgs e)
    {
        var comando = Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(comando)) return;
        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                EscalaInterface.Aumentar();
                break;
            case Key.OemMinus or Key.Subtract:
                EscalaInterface.Diminuir();
                break;
            case Key.D0 or Key.NumPad0:
                EscalaInterface.Restaurar();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private async void ExportarBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var arquivo = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar backup",
            SuggestedFileName = $"escalas-backup-{DateTime.Now:yyyyMMdd-HHmm}.db",
            DefaultExtension = "db",
            FileTypeChoices = [new FilePickerFileType("Banco do Escalas") { Patterns = ["*.db"] }],
        });
        if (arquivo?.TryGetLocalPath() is { } caminho)
            vm.ExportarBackup(caminho);
    }
}
