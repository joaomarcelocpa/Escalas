using Avalonia;
using Avalonia.Controls;

namespace Escalas.Desktop.Servicos;

/// <summary>
/// Tamanho e posição da janela principal: abre centralizada, sem passar de 90% da tela, e lembra
/// o tamanho/posição/maximizada da última vez (em ui.json), para não abrir ocupando a tela inteira
/// em monitores grandes nem estourar em notebooks.
/// </summary>
public static class JanelaPrincipal
{
    private const double FracaoInicialTela = 0.9;

    public static void Configurar(Window janela, IPreferenciasUi preferencias)
    {
        var area = EscalaInterface.AreaUtil(janela);
        var salva = preferencias.Janela;

        if (salva is not null && Valida(janela, salva))
        {
            janela.WindowStartupLocation = WindowStartupLocation.Manual;
            janela.Position = new PixelPoint(salva.X, salva.Y);
            janela.Width = Math.Min(salva.Largura, area.Width);
            janela.Height = Math.Min(salva.Altura, area.Height);
            if (salva.Maximizada) janela.WindowState = WindowState.Maximized;
        }
        else
        {
            janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            janela.Width = Math.Min(janela.Width, area.Width * FracaoInicialTela);
            janela.Height = Math.Min(janela.Height, area.Height * FracaoInicialTela);
        }

        // Guarda o último tamanho/posição "normal" (não maximizado) para restaurar depois.
        double largura = janela.Width, altura = janela.Height;
        var posicao = janela.Position;
        janela.PositionChanged += (_, e) =>
        {
            if (janela.WindowState == WindowState.Normal) posicao = e.Point;
        };
        janela.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.ClientSizeProperty && janela.WindowState == WindowState.Normal)
                (largura, altura) = (janela.ClientSize.Width, janela.ClientSize.Height);
        };
        janela.Closing += (_, _) =>
        {
            if (janela.WindowState == WindowState.Normal) posicao = janela.Position;
            preferencias.Janela = new GeometriaJanela(posicao.X, posicao.Y, largura, altura,
                janela.WindowState == WindowState.Maximized);
        };
    }

    /// <summary>Só restaura se o tamanho for razoável e a barra de título cair dentro de alguma tela ligada.</summary>
    private static bool Valida(Window janela, GeometriaJanela g)
    {
        if (!double.IsFinite(g.Largura) || !double.IsFinite(g.Altura) || g.Largura < 400 || g.Altura < 300) return false;
        var titulo = new PixelRect(g.X, g.Y, 200, 40);
        return janela.Screens.All.Any(t => t.WorkingArea.Intersects(titulo));
    }
}
