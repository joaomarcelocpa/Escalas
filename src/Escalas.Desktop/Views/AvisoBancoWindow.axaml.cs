using Avalonia.Controls;

namespace Escalas.Desktop.Views;

public partial class AvisoBancoWindow : Window
{
    public AvisoBancoWindow()
    {
        InitializeComponent();
        Fechar.Click += (_, _) => Close();
    }

    public AvisoBancoWindow(string mensagem) : this() => Mensagem.Text = mensagem;
}
