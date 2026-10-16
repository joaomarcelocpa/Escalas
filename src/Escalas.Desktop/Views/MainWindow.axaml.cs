using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
