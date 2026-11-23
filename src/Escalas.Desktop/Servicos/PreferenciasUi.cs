using System.Text.Json;
using Escalas.Infrastructure.Nucleo;

namespace Escalas.Desktop.Servicos;

/// <summary>Preferências de interface (só visuais) guardadas em ui.json na pasta de dados do app.</summary>
public interface IPreferenciasUi
{
    bool SidebarRecolhida { get; set; }
}

public sealed class PreferenciasUi : IPreferenciasUi
{
    private readonly string _arquivo;
    private Dados _dados;

    public PreferenciasUi(CaminhosApp caminhos) : this(Path.Combine(caminhos.DiretorioDados, "ui.json")) { }

    public PreferenciasUi(string arquivo)
    {
        _arquivo = arquivo;
        _dados = Carregar(arquivo);
    }

    public bool SidebarRecolhida
    {
        get => _dados.SidebarRecolhida;
        set
        {
            if (_dados.SidebarRecolhida == value) return;
            _dados = _dados with { SidebarRecolhida = value };
            Salvar();
        }
    }

    private static Dados Carregar(string arquivo)
    {
        try
        {
            return File.Exists(arquivo)
                ? JsonSerializer.Deserialize<Dados>(File.ReadAllText(arquivo)) ?? new Dados()
                : new Dados();
        }
        catch
        {
            return new Dados(); // arquivo corrompido: volta ao padrão, preferência visual não deve travar o app
        }
    }

    private void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_arquivo)!);
            File.WriteAllText(_arquivo, JsonSerializer.Serialize(_dados));
        }
        catch
        {
            // Falha ao gravar preferência visual não é crítica.
        }
    }

    private sealed record Dados
    {
        public bool SidebarRecolhida { get; init; }
    }
}
