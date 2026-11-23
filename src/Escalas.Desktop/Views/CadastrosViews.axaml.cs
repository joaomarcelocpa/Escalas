using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Views;

public partial class SetoresView : UserControl
{
    public SetoresView() => InitializeComponent();
}

public partial class FuncionariosView : UserControl
{
    public FuncionariosView() => InitializeComponent();

    private async void EscolherFoto_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FuncionariosViewModel vm || TopLevel.GetTopLevel(this) is not { } topo) return;
        var arquivos = await topo.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Escolher foto do funcionário",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (arquivos.Count == 0) return;
        await using var stream = await arquivos[0].OpenReadAsync();
        vm.DefinirFoto(stream);
    }
}
