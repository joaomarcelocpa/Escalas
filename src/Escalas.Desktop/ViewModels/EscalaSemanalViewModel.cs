using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Calendario;

namespace Escalas.Desktop.ViewModels;

/// <summary>Funcionário alocado (ou disponível) em um dia do modelo semanal. <see cref="Origem"/> nulo = veio de "Disponíveis".</summary>
public record FuncionarioPadraoItem(int Id, string Nome, string? Detalhe, DayOfWeek? Origem, byte[]? Foto = null);

public class SetorPadraoViewModel(SetorDiaDto dto, DayOfWeek dia)
{
    public string Nome { get; } = dto.Nome;
    public string Contagem { get; } = dto.Escalados.Count.ToString();
    public IReadOnlyList<FuncionarioPadraoItem> Escalados { get; } =
        dto.Escalados.Select(f => new FuncionarioPadraoItem(f.Id, f.Nome, dto.Nome, dia, f.Foto)).ToList();
}

public class DiaPadraoViewModel
{
    public DiaPadraoViewModel(DiaPadraoDto dto, int? setorId)
    {
        Dia = dto.Dia;
        Titulo = new CultureInfo("pt-BR").TextInfo.ToTitleCase(
            new CultureInfo("pt-BR").DateTimeFormat.GetDayName(dto.Dia));
        Setores = dto.Setores.Where(s => setorId is null || s.SetorId == setorId)
            .Select(s => new SetorPadraoViewModel(s, dto.Dia)).ToList();
        Disponiveis = dto.Disponiveis.Where(f => setorId is null || f.SetorId == setorId)
            .Select(f => new FuncionarioPadraoItem(f.Id, f.Nome, f.SetorNome, null, f.Foto)).ToList();
        TextoDisponiveis = $"Disponíveis ({Disponiveis.Count})";
    }

    public DayOfWeek Dia { get; }
    public string Titulo { get; }
    public IReadOnlyList<SetorPadraoViewModel> Setores { get; }
    public IReadOnlyList<FuncionarioPadraoItem> Disponiveis { get; }
    public string TextoDisponiveis { get; }
}

/// <summary>Tela de montagem do modelo semanal (segunda a sábado), que vale para todas as semanas do calendário.</summary>
public partial class EscalaSemanalViewModel : ViewModelBase
{
    private static readonly SetorFiltro TodosSetores = new(null, "Todos os setores");

    private readonly IEscalaService? _calendario;
    private readonly IEscalaPadraoService? _servico;
    private bool _atualizandoSetores;

    public ObservableCollection<FilialResumo> Filiais { get; } = [];
    public ObservableCollection<SetorFiltro> Setores { get; } = [];
    public ObservableCollection<DiaPadraoViewModel> Dias { get; } = [];

    [ObservableProperty] public partial FilialResumo? FilialSelecionada { get; set; }
    [ObservableProperty] public partial SetorFiltro? SetorSelecionado { get; set; }
    [ObservableProperty] public partial string? Mensagem { get; set; }
    [ObservableProperty] public partial bool SemFiliais { get; set; }

    /// <summary>Construtor para o previewer.</summary>
    public EscalaSemanalViewModel() { }

    public EscalaSemanalViewModel(IEscalaService calendario, IEscalaPadraoService servico)
    {
        _calendario = calendario;
        _servico = servico;
        _ = CarregarAsync();
    }

    partial void OnFilialSelecionadaChanged(FilialResumo? value) => _ = RecarregarAsync(refazerSetores: true);

    partial void OnSetorSelecionadoChanged(SetorFiltro? value)
    {
        if (!_atualizandoSetores) _ = RecarregarAsync();
    }

    private async Task CarregarAsync()
    {
        foreach (var f in await _calendario!.ListarFiliaisAsync()) Filiais.Add(f);
        SemFiliais = Filiais.Count == 0;
        FilialSelecionada = Filiais.FirstOrDefault();
    }

    private async Task RecarregarAsync(bool refazerSetores = false)
    {
        Dias.Clear();
        if (_servico is null || FilialSelecionada is null) return;

        var semana = await _servico.ObterAsync(FilialSelecionada.Id);
        if (refazerSetores)
        {
            _atualizandoSetores = true;
            Setores.Clear();
            Setores.Add(TodosSetores);
            foreach (var st in semana.Dias.FirstOrDefault()?.Setores ?? [])
                Setores.Add(new SetorFiltro(st.SetorId, st.Nome));
            SetorSelecionado = TodosSetores;
            _atualizandoSetores = false;
        }
        foreach (var d in semana.Dias) Dias.Add(new DiaPadraoViewModel(d, SetorSelecionado?.Id));
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

    [RelayCommand]
    private Task DesalocarAsync(FuncionarioPadraoItem item) => SoltarEmDisponiveisAsync(item.Id, item.Origem);

    [RelayCommand]
    private Task AlocarTodosOsDiasAsync(FuncionarioPadraoItem item) =>
        _servico is null ? Task.CompletedTask : Aplicar(_servico.AlocarTodosOsDiasAsync(item.Id));
}
