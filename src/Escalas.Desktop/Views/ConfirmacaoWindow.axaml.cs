using Avalonia.Controls;

namespace Escalas.Desktop.Views;

public partial class ConfirmacaoWindow : Window
{
    public ConfirmacaoWindow() => InitializeComponent();

    public ConfirmacaoWindow(string titulo, string mensagem, string textoConfirmar) : this()
    {
        Title = titulo;
        Titulo.Text = titulo;
        Mensagem.Text = mensagem;
        Confirmar.Content = textoConfirmar;
        Cancelar.Click += (_, _) => Close(false);
        Confirmar.Click += (_, _) => Close(true);
    }
}
