using Avalonia.Media;
using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Navegacao;

/// <summary>Entrada do menu lateral. O ViewModel é resolvido via DI; a View é achada pelo ViewLocator (XxxViewModel → XxxView).
/// <paramref name="Icone"/> é o dado de caminho (viewBox 24x24) exibido ao lado do título e sozinho com a barra recolhida.</summary>
public record ItemNavegacao(string Titulo, Type ViewModel, string Icone = Icones.Padrao)
{
    public Geometry Geometria { get; } = Geometry.Parse(Icone);
}
