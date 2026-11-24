using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Escalas.Desktop.Servicos;

/// <summary>
/// No macOS o Dock ignora o Icon da janela: o ícone vem do bundle .app, que não existe
/// ao rodar pelo Rider/dotnet run. Aqui definimos o ícone do Dock direto no NSApplication.
/// Em Windows/Linux não faz nada (lá vale o Icon da janela e o ApplicationIcon do .exe).
/// </summary>
internal static class IconeDock
{
    private const string ObjC = "/usr/lib/libobjc.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string nome);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string nome);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Enviar(IntPtr alvo, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Enviar(IntPtr alvo, IntPtr sel, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Enviar(IntPtr alvo, IntPtr sel, IntPtr arg1, nuint arg2);

    public static void Aplicar(string recurso = "avares://Escalas.Desktop/Assets/icone.png")
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            using var origem = AssetLoader.Open(new Uri(recurso));
            using var memoria = new MemoryStream();
            origem.CopyTo(memoria);
            var bytes = memoria.ToArray();

            var buffer = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var dados = Enviar(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"),
                    buffer.AddrOfPinnedObject(), (nuint)bytes.Length);
                var imagem = Enviar(Enviar(objc_getClass("NSImage"), sel_registerName("alloc")),
                    sel_registerName("initWithData:"), dados);
                if (imagem == IntPtr.Zero) return;
                var app = Enviar(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                Enviar(app, sel_registerName("setApplicationIconImage:"), imagem);
            }
            finally
            {
                buffer.Free();
            }
        }
        catch
        {
            // Ícone é cosmético: se falhar, o app segue com o ícone padrão.
        }
    }
}
