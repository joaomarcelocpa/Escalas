using Avalonia.Controls.ApplicationLifetimes;
using Escalas.Desktop.Views;

namespace Escalas.Desktop.Servicos;

public class DialogoConfirmacao : IDialogoConfirmacao
{
    public async Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoConfirmar = "Confirmar")
    {
        var dono = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (dono is null) return false;
        return await new ConfirmacaoWindow(titulo, mensagem, textoConfirmar).ShowDialog<bool>(dono);
    }
}
