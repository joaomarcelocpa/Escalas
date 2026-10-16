namespace Escalas.Application.Cadastros;

/// <summary>Violação de regra de cadastro com mensagem pronta para exibir ao usuário.</summary>
public class CadastroException(string message) : Exception(message);
