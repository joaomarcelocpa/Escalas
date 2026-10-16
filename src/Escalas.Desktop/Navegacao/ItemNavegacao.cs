using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Navegacao;

/// <summary>Entrada do menu lateral. O ViewModel é resolvido via DI; a View é achada pelo ViewLocator (XxxViewModel → XxxView).</summary>
public record ItemNavegacao(string Titulo, Type ViewModel);
