using CommunityToolkit.Mvvm.ComponentModel;
using Escalas.Application.Cadastros;

namespace Escalas.Desktop.ViewModels;

/// <summary>Base dos ViewModels de cadastro: centraliza exibição de erros de validação.</summary>
public abstract partial class CadastroViewModelBase : ViewModelBase
{
    [ObservableProperty]
    public partial string? Erro { get; set; }

    protected async Task ExecutarAsync(Func<Task> acao)
    {
        Erro = null;
        try
        {
            await acao();
        }
        catch (CadastroException ex)
        {
            Erro = ex.Message;
        }
        catch (Exception ex)
        {
            Erro = "Falha inesperada: " + ex.Message;
        }
    }
}
