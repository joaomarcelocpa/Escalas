using CommunityToolkit.Mvvm.ComponentModel;

namespace Escalas.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Titulo { get; set; } = "Escalas";

    [ObservableProperty]
    public partial string Subtitulo { get; set; } = "Controle de escalas de trabalho";
}
