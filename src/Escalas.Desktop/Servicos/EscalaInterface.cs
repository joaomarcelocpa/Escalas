using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Escalas.Desktop.Servicos;

/// <summary>
/// Zoom da interface (todas as janelas do app). O tamanho físico de cada coisa depende da tela e da escala do
/// sistema operacional, então em monitores grandes o app pode parecer grande demais; o zoom deixa o usuário
/// reduzir ou aumentar tudo proporcionalmente (Ctrl/⌘ + − / = / 0 ou os botões da barra lateral).
/// Cada janela precisa passar por <see cref="Preparar{T}"/> antes de ser mostrada.
/// </summary>
public static class EscalaInterface
{
    public const double Minimo = 0.7;
    public const double Maximo = 1.5;
    public const double Passo = 0.1;

    /// <summary>Fração da área útil da tela que uma janela pode ocupar, para nunca nascer maior que o monitor.</summary>
    private const double FracaoMaximaTela = 0.95;

    private sealed record Janela(
        Window W, LayoutTransformControl Moldura, Window? Dono, bool AjustarTamanho,
        double? LarguraBase, double? AlturaBase, double MinLarguraBase, double MinAlturaBase);

    private static readonly List<Janela> Janelas = [];

    public static double Atual { get; private set; } = 1;

    public static event Action? Mudou;

    public static string Texto => $"{Math.Round(Atual * 100):0}%";

    public static double Normalizar(double valor) =>
        double.IsFinite(valor) && valor > 0 ? Math.Round(Math.Clamp(valor, Minimo, Maximo), 2) : 1;

    public static void Definir(double valor)
    {
        valor = Normalizar(valor);
        if (Math.Abs(valor - Atual) < 0.001) return;
        Atual = valor;
        foreach (var j in Janelas.ToList()) Aplicar(j);
        Mudou?.Invoke();
    }

    public static void Aumentar() => Definir(Atual + Passo);
    public static void Diminuir() => Definir(Atual - Passo);
    public static void Restaurar() => Definir(1);

    /// <summary>
    /// Envolve o conteúdo da janela numa moldura com o zoom atual e passa a acompanhar as mudanças de zoom.
    /// Com <paramref name="ajustarTamanho"/>, a largura/altura definidas no XAML também acompanham o zoom
    /// (popups de tamanho fixo); a janela principal controla o próprio tamanho.
    /// Em qualquer caso a janela é limitada à área útil da tela.
    /// </summary>
    public static T Preparar<T>(T janela, Window? dono = null, bool ajustarTamanho = true) where T : Window
    {
        if (janela.Content is not Control conteudo || conteudo is LayoutTransformControl) return janela;

        janela.Content = null;
        var moldura = new LayoutTransformControl { Child = conteudo };
        janela.Content = moldura;

        var j = new Janela(janela, moldura, dono, ajustarTamanho,
            double.IsNaN(janela.Width) ? null : janela.Width,
            double.IsNaN(janela.Height) ? null : janela.Height,
            janela.MinWidth, janela.MinHeight);
        Janelas.Add(j);
        janela.Closed += (_, _) => Janelas.Remove(j);
        Aplicar(j);
        return janela;
    }

    /// <summary>Área útil (em unidades lógicas) da tela do dono, ou da tela principal.</summary>
    public static Size AreaUtil(Window janela, Window? dono = null)
    {
        var telas = (dono ?? janela).Screens;
        var tela = (dono is not null ? telas.ScreenFromWindow(dono) : null) ?? telas.Primary;
        if (tela is null || tela.Scaling <= 0) return new Size(double.PositiveInfinity, double.PositiveInfinity);
        return new Size(tela.WorkingArea.Width / tela.Scaling, tela.WorkingArea.Height / tela.Scaling);
    }

    private static void Aplicar(Janela j)
    {
        var s = Atual;
        j.Moldura.LayoutTransform = Math.Abs(s - 1) < 0.001 ? null : new ScaleTransform(s, s);

        var area = AreaUtil(j.W, j.Dono);
        var maxLargura = area.Width * FracaoMaximaTela;
        var maxAltura = area.Height * FracaoMaximaTela;

        j.W.MinWidth = Math.Min(j.MinLarguraBase * s, maxLargura);
        j.W.MinHeight = Math.Min(j.MinAlturaBase * s, maxAltura);
        if (!j.AjustarTamanho) return;

        if (j.LarguraBase is { } l) j.W.Width = Math.Min(l * s, maxLargura);
        if (j.AlturaBase is { } a && !j.W.SizeToContent.HasFlag(SizeToContent.Height))
            j.W.Height = Math.Min(a * s, maxAltura);
        if (j.W.SizeToContent.HasFlag(SizeToContent.Height)) j.W.MaxHeight = maxAltura;
    }
}
