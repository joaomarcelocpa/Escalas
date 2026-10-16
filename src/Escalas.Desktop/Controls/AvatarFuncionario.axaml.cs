using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Escalas.Desktop.Controls;

/// <summary>Foto circular do funcionário; sem foto, mostra as iniciais. Tamanho definido por Width/Height.</summary>
public partial class AvatarFuncionario : UserControl
{
    public static readonly StyledProperty<byte[]?> FotoProperty =
        AvaloniaProperty.Register<AvatarFuncionario, byte[]?>(nameof(Foto));

    public static readonly StyledProperty<string?> NomeProperty =
        AvaloniaProperty.Register<AvatarFuncionario, string?>(nameof(Nome));

    public byte[]? Foto { get => GetValue(FotoProperty); set => SetValue(FotoProperty, value); }
    public string? Nome { get => GetValue(NomeProperty); set => SetValue(NomeProperty, value); }

    public AvatarFuncionario() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FotoProperty) AtualizarFoto();
        else if (change.Property == NomeProperty) AtualizarIniciais();
        else if (change.Property == WidthProperty || change.Property == HeightProperty) AtualizarForma();
    }

    private void AtualizarForma()
    {
        // Width e Height chegam em momentos diferentes; enquanto um for NaN, não há o que calcular.
        var lado = Math.Min(Width, Height);
        if (double.IsNaN(lado) || lado <= 0) return;
        Moldura.CornerRadius = new CornerRadius(lado / 2);
        Iniciais.FontSize = Math.Max(8, lado * 0.4);
    }

    private void AtualizarIniciais()
    {
        var partes = (Nome ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Iniciais.Text = partes.Length switch
        {
            0 => "",
            1 => partes[0][..1].ToUpperInvariant(),
            _ => (partes[0][..1] + partes[^1][..1]).ToUpperInvariant(),
        };
    }

    private void AtualizarFoto()
    {
        Imagem.Source = null;
        if (Foto is not { Length: > 0 } bytes) return;
        Imagem.Source = ObterBitmap(bytes);
    }

    // Cache por conteúdo: trocar de semana recarrega os bytes do banco (arrays novos), mas a foto é a mesma.
    // Decodificar em tamanho reduzido evita gastar a thread da tela com fotos grandes.
    private static readonly Dictionary<string, Bitmap?> Cache = [];

    private static Bitmap? ObterBitmap(byte[] bytes)
    {
        var chave = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));
        if (Cache.TryGetValue(chave, out var existente)) return existente;
        Bitmap? bitmap = null;
        try { bitmap = Bitmap.DecodeToWidth(new MemoryStream(bytes), 96); }
        catch { /* foto corrompida: cai nas iniciais */ }
        Cache[chave] = bitmap;
        return bitmap;
    }
}
