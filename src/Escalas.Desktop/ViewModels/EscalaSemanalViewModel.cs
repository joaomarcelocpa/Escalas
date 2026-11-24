using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Calendario;
using Escalas.Desktop.Servicos;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

/// <summary>Funcionário alocado (ou disponível) em um dia do modelo semanal. <see cref="Origem"/> nulo = veio de "Disponíveis".</summary>
public record FuncionarioPadraoItem(int Id, string Nome, string? Detalhe, DayOfWeek? Origem, byte[]? Foto = null);

/// <summary>Cobertura de folga em um dia do modelo semanal (quem cobre, quem pode cobrir ou quem saiu para cobrir).
/// Tipo próprio para não entrar no arrastar e soltar.</summary>
public record CoberturaPadraoItem(int Id, string Nome, string Detalhe, DayOfWeek Dia, byte[]? Foto = null);

public class DiaPadraoViewModel
{
    private readonly Action<CoberturaPadraoItem>? _aoEscolherCobertura;

    public DiaPadraoViewModel(DiaPadraoDto dto, ExpedienteDto? expediente = null, Action<CoberturaPadraoItem>? aoEscolherCobertura = null)
    {
        _aoEscolherCobertura = aoEscolherCobertura;
        Dia = dto.Dia;
        Titulo = new CultureInfo("pt-BR").TextInfo.ToTitleCase(
            new CultureInfo("pt-BR").DateTimeFormat.GetDayName(dto.Dia));
        Escalados = dto.Setores
            .SelectMany(s => s.Escalados.Select(f => new FuncionarioPadraoItem(f.Id, f.Nome, s.Nome, dto.Dia, f.Foto)))
            .OrderBy(f => f.Nome).ToList();
        Contagem = (Escalados.Count + dto.Cobrindo.Count).ToString(); // quem cobre folga também trabalha aqui no dia
        Disponiveis = dto.Disponiveis
            .Select(f => new FuncionarioPadraoItem(f.Id, f.Nome, f.SetorNome, null, f.Foto)).ToList();
        TextoDisponiveis = $"Disponíveis ({Disponiveis.Count})";
        Cobrindo = dto.Cobrindo
            .Select(c => new CoberturaPadraoItem(c.Id, c.Nome, $"Cobertura · {Filiais.Nome(c.Filial)}", dto.Dia, c.Foto)).ToList();
        Candidatos = dto.Candidatos
            .Select(c => new CoberturaPadraoItem(c.Id, c.Nome, Detalhar(Filiais.Nome(c.Filial), c.Setor), dto.Dia, c.Foto)).ToList();
        Fora = dto.Fora
            .Select(c => new CoberturaPadraoItem(c.Id, c.Nome, $"Cobre {Filiais.Nome(c.Filial)}", dto.Dia, c.Foto)).ToList();
        SemExpediente = expediente is null;
        TextoExpediente = expediente is null
            ? "Sem expediente"
            : $"{RegraHoras.FormatarIntervalo(expediente.Inicio, expediente.Fim)} · {RegraHoras.Formatar(RegraHoras.HorasTrabalhadas(expediente.Inicio, expediente.Fim))}";
    }

    public DayOfWeek Dia { get; }
    public string Titulo { get; }
    public IReadOnlyList<FuncionarioPadraoItem> Escalados { get; }
    public string Contagem { get; }
    public IReadOnlyList<FuncionarioPadraoItem> Disponiveis { get; }
    public string TextoDisponiveis { get; }
    /// <summary>Expediente do dia da semana ("08:00–17:30 · 9h30") ou "Sem expediente".</summary>
    public string TextoExpediente { get; }
    public bool SemExpediente { get; }

    /// <summary>Funcionários de outra filial cobrindo folga aqui neste dia.</summary>
    public IReadOnlyList<CoberturaPadraoItem> Cobrindo { get; }
    /// <summary>Quem pode cobrir (filial extra = esta); só existe quando algum fixo folga no dia.</summary>
    public IReadOnlyList<CoberturaPadraoItem> Candidatos { get; }
    /// <summary>Funcionários desta filial que estão cobrindo outra filial neste dia.</summary>
    public IReadOnlyList<CoberturaPadraoItem> Fora { get; }
    public bool TemCobrindo => Cobrindo.Count > 0;
    public bool TemCandidatos => Candidatos.Count > 0;
    public bool TemFora => Fora.Count > 0;
    public bool MostrarCobertura => TemCobrindo || TemCandidatos || TemFora;

    /// <summary>Escolha na caixa "Cobrir folga com…": aplica a cobertura (a tela recarrega e a caixa volta vazia).</summary>
    public CoberturaPadraoItem? CandidatoEscolhido
    {
        get => null;
        set { if (value is not null) _aoEscolherCobertura?.Invoke(value); }
    }

    private static string Detalhar(string filial, string setor) => string.IsNullOrEmpty(setor) ? filial : $"{filial} · {setor}";
}

/// <summary>Tela de montagem do modelo semanal (segunda a sábado), que vale para todas as semanas do calendário.</summary>
public partial class EscalaSemanalViewModel : ViewModelBase
{
    private readonly IEscalaPadraoService? _servico;
    private readonly IExpedienteService? _expedientes;
    private readonly IDialogosHorario? _dialogosHorario;
    private readonly IExportacaoEscalaService? _exportacao;

    /// <summary>Filiais fixas.</summary>
    public IReadOnlyList<FilialOpcao> Filiais { get; } = global::Escalas.Domain.Entidades.Filiais.Todas;
    public ObservableCollection<DiaPadraoViewModel> Dias { get; } = [];

    [ObservableProperty] public partial FilialOpcao? FilialSelecionada { get; set; }
    [ObservableProperty] public partial string? Mensagem { get; set; }

    /// <summary>Construtor para o previewer.</summary>
    public EscalaSemanalViewModel() { }

    public EscalaSemanalViewModel(IEscalaPadraoService servico, IExpedienteService expedientes, IDialogosHorario dialogosHorario,
        IExportacaoEscalaService exportacao)
    {
        _exportacao = exportacao;
        _servico = servico;
        _expedientes = expedientes;
        _dialogosHorario = dialogosHorario;
        FilialSelecionada = Filiais[0];
    }

    partial void OnFilialSelecionadaChanged(FilialOpcao? value) => _ = RecarregarAsync();

    private async Task RecarregarAsync()
    {
        Dias.Clear();
        if (_servico is null || FilialSelecionada is null) return;

        var semana = await _servico.ObterAsync(FilialSelecionada.Id);
        var expedientes = _expedientes is null
            ? new Dictionary<DayOfWeek, ExpedienteDto>()
            : (await _expedientes.ObterAsync()).ToDictionary(e => e.Dia);
        foreach (var d in semana.Dias)
            Dias.Add(new DiaPadraoViewModel(d, expedientes.GetValueOrDefault(d.Dia), c => _ = CobrirAsync(c)));
    }

    private async Task Aplicar(Task<ResultadoOperacao> operacao)
    {
        var r = await operacao;
        Mensagem = r.Ok ? null : r.Erro;
        await RecarregarAsync();
    }

    /// <summary>Chamado pela View ao soltar um funcionário sobre um dia.</summary>
    public Task SoltarEmDiaAsync(int funcionarioId, DayOfWeek? origem, DayOfWeek destino)
    {
        if (_servico is null) return Task.CompletedTask;
        if (origem is null) return Aplicar(_servico.AlocarAsync(funcionarioId, destino));
        if (origem == destino) return Task.CompletedTask;
        return Aplicar(_servico.MoverAsync(funcionarioId, origem.Value, destino));
    }

    /// <summary>Chamado pela View ao soltar um funcionário alocado na lista de disponíveis.</summary>
    public Task SoltarEmDisponiveisAsync(int funcionarioId, DayOfWeek? origem) =>
        _servico is null || origem is null ? Task.CompletedTask : Aplicar(_servico.DesalocarAsync(funcionarioId, origem.Value));

    /// <summary>Nome sugerido para a planilha exportada.</summary>
    public string NomeArquivoPlanilha => $"escala-semanal-{DateTime.Today:yyyy-MM-dd}.xlsx";

    /// <summary>Planilha .xlsx com a escala de todas as filiais (uma aba por filial). Null se não houver serviço (previewer).</summary>
    public async Task<byte[]?> GerarPlanilhaAsync()
    {
        if (_exportacao is null) return null;
        Mensagem = null;
        try
        {
            return await _exportacao.GerarPlanilhaAsync();
        }
        catch (Exception)
        {
            Mensagem = "Não foi possível gerar a planilha.";
            return null;
        }
    }

    /// <summary>Edita o expediente (início e fim) de cada dia da semana; vale para todas as filiais e semanas.</summary>
    [RelayCommand]
    private async Task EditarExpedienteAsync()
    {
        if (_dialogosHorario is null) return;
        Mensagem = null;
        if (await _dialogosHorario.EditarExpedienteAsync()) await RecarregarAsync();
    }

    [RelayCommand]
    private Task DesalocarAsync(FuncionarioPadraoItem item) => SoltarEmDisponiveisAsync(item.Id, item.Origem);

    /// <summary>O candidato sai da filial dele neste dia e cobre a filial exibida.</summary>
    private Task CobrirAsync(CoberturaPadraoItem item) =>
        _servico is null || FilialSelecionada is null
            ? Task.CompletedTask
            : Aplicar(_servico.CobrirAsync(item.Id, FilialSelecionada.Id, item.Dia));

    /// <summary>Desfaz a cobertura: o funcionário volta para a própria filial neste dia.</summary>
    [RelayCommand]
    private Task RemoverCoberturaAsync(CoberturaPadraoItem item) =>
        _servico is null ? Task.CompletedTask : Aplicar(_servico.RemoverCoberturaAsync(item.Id, item.Dia));

    [RelayCommand]
    private Task AlocarTodosOsDiasAsync(FuncionarioPadraoItem item) =>
        _servico is null ? Task.CompletedTask : Aplicar(_servico.AlocarTodosOsDiasAsync(item.Id));
}
