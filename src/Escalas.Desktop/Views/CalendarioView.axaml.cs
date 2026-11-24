using Avalonia.Controls;
using Avalonia.Input;
using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Views;

/// <summary>Calendário somente leitura: mostra quem está escalado e de folga, sem arrastar nem remover.
/// Clicar em um funcionário abre as horas dele na semana.</summary>
public partial class CalendarioView : UserControl
{
    public CalendarioView()
    {
        InitializeComponent();
    }

    private void Funcionario_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FuncionarioItem item } && DataContext is CalendarioViewModel vm)
            vm.VerHorasCommand.Execute(item);
    }
}
