using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Nucleo;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IInicializadorBanco? _inicializador;
    private readonly Func<IAutenticacaoService>? _auth;

    /// <summary>Disparado após login válido.</summary>
    public event Action<Usuario>? LoginConcluido;

    /// <summary>Verdadeiro quando o banco foi aberto (usado para decidir o backup ao sair).</summary>
    public bool BancoPronto { get; private set; }

    [ObservableProperty]
    public partial string Titulo { get; set; } = "Escalas";

    /// <summary>Preenchida quando o banco não pode ser aberto (sem formulário de login).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormularioVisivel), nameof(ProblemaVisivel))]
    public partial string? Problema { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormularioVisivel))]
    public partial bool Carregando { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    public partial string Login { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    public partial string Senha { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErroVisivel))]
    public partial string? Erro { get; set; }

    public bool FormularioVisivel => !Carregando && Problema is null;
    public bool ProblemaVisivel => Problema is not null;
    public bool ErroVisivel => Erro is not null;

    /// <summary>Construtor para o previewer.</summary>
    public LoginViewModel() => Carregando = false;

    public LoginViewModel(IInicializadorBanco inicializador, Func<IAutenticacaoService> auth)
    {
        _inicializador = inicializador;
        _auth = auth;
    }

    public async Task CarregarAsync()
    {
        Carregando = true;
        var r = await _inicializador!.InicializarAsync();
        BancoPronto = r.Situacao == SituacaoBanco.Pronto;
        Problema = BancoPronto ? null : r.Mensagem ?? "Não foi possível abrir o banco de dados.";
        Carregando = false;
    }

    private bool PodeEntrar() => !string.IsNullOrWhiteSpace(Login) && Senha.Length > 0;

    [RelayCommand(CanExecute = nameof(PodeEntrar))]
    private async Task EntrarAsync()
    {
        Erro = null;
        var usuario = await _auth!().EntrarAsync(Login, Senha);
        if (usuario is null)
        {
            Erro = "Usuário ou senha incorretos.";
            Senha = "";
            return;
        }
        Senha = "";
        LoginConcluido?.Invoke(usuario);
    }
}
