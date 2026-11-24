using Avalonia.Controls;
using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Views;

public partial class ExpedienteWindow : Window
{
    public ExpedienteWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ExpedienteSemanaViewModel vm) vm.Concluido += salvou => Close(salvou);
    }
}

public partial class HorasFuncionarioWindow : Window
{
    public HorasFuncionarioWindow()
    {
        InitializeComponent();
        Fechar.Click += (_, _) => Close();
    }
}

public partial class ResumoSemanalWindow : Window
{
    public ResumoSemanalWindow()
    {
        InitializeComponent();
        Fechar.Click += (_, _) => Close();
    }
}
