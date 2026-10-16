namespace Escalas.Desktop.Servicos;

/// <summary>Pede confirmação ao usuário em um modal antes de alterar a escala.</summary>
public interface IDialogoConfirmacao
{
    Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoConfirmar = "Confirmar");
}
