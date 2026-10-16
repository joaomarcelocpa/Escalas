using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Calendario;
using Escalas.Domain.Calendario;

namespace Escalas.Desktop.ViewModels;

/// <summary>Funcionário exibido em um dia. <see cref="Origem"/> é nulo quando vem da lista de disponíveis.</summary>
public record FuncionarioItem(int Id, string Nome, string? Detalhe, DateOnly? Origem, byte[]? Foto = null);

public class SetorDiaViewModel(SetorDiaDto dto, DateOnly data)
{
    public string Nome { get; } = dto.Nome;
    public string Contagem { get; } = $"{dto.Escalados.Count}";
    // Mínimo por setor desativado por enquanto (escala livre); a regra segue no Domain.
    public bool AbaixoDoMinimo => false;
    public IReadOnlyList<FuncionarioItem> Escalados { get; } =
        dto.Escalados.Select(f => new FuncionarioItem(f.Id, f.Nome, dto.Nome, data, f.Foto)).ToList();
}

/// <summary>Opção do filtro de setor; <see cref="Id"/> nulo significa todos os setores.</summary>
public record SetorFiltro(int? Id, string Nome);

public class DiaViewModel
{
    private static readonly string[] DiasSemana = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

    public DiaViewModel(DiaDto dto, int? setorId = null)
    {
        Data = dto.Data;
        DiaSemana = DiasSemana[(int)dto.Data.DayOfWeek];
        Numero = dto.Data.Day.ToString("00");
        NomeFeriado = dto.NomeFeriado;
        Fechado = dto.Fechado;
        TextoFechar = dto.Fechado ? "Reabrir" : "Fechar";
        Setores = dto.Setores.Where(s => setorId is null || s.SetorId == setorId).Select(s => new SetorDiaViewModel(s, dto.Data)).ToList();
        Disponiveis = dto.Disponiveis.Where(f => setorId is null || f.SetorId == setorId).Select(f => new FuncionarioItem(f.Id, f.Nome, f.SetorNome, null, f.Foto)).ToList();
        TextoDisponiveis = $"Disponíveis ({Disponiveis.Count})";
    }

    public DateOnly Data { get; }
    public string DiaSemana { get; }
    public string Numero { get; }
    public string? NomeFeriado { get; }
    public bool TemFeriado => NomeFeriado is not null;
    public bool Fechado { get; }
    public string TextoFechar { get; }
    public IReadOnlyList<SetorDiaViewModel> Setores { get; }
    public IReadOnlyList<FuncionarioItem> Disponiveis { get; }
    public string TextoDisponiveis { get; }
}

public partial class CalendarioViewModel : ViewModelBase
{
    private static readonly string[] Meses =
        ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    private readonly IEscalaService? _servico;
    private DateOnly _inicioSemana = RegraEscala.InicioDaSemana(DateOnly.FromDateTime(DateTime.Today));

    public ObservableCollection<FilialResumo> Filiais { get; } = [];
    public ObservableCollection<DiaViewModel> Dias { get; } = [];
    public ObservableCollection<SetorFiltro> Setores { get; } = [];

    private static readonly SetorFiltro TodosSetores = new(null, "Todos os setores");
    private bool _atualizandoSetores;

    [ObservableProperty]
    public partial SetorFiltro? SetorSelecionado { get; set; }

    [ObservableProperty]
    public partial FilialResumo? FilialSelecionada { get; set; }

    [ObservableProperty]
    public partial string TituloSemana { get; set; } = "";

    [ObservableProperty]
    public partial string? Mensagem { get; set; }

    [ObservableProperty]
    public partial bool SemFiliais { get; set; }

    /// <summary>Construtor para o previewer.</summary>
    public CalendarioViewModel() => AtualizarTitulo();

    public CalendarioViewModel(IEscalaService servico)
    {
        _servico = servico;
        AtualizarTitulo();
        _ = CarregarAsync();
    }

    partial void OnFilialSelecionadaChanged(FilialResumo? value) => _ = RecarregarAsync(refazerSetores: true);

    partial void OnSetorSelecionadoChanged(SetorFiltro? value)
    {
        if (!_atualizandoSetores) _ = RecarregarAsync();
    }

    private async Task CarregarAsync()
    {
        var filiais = await _servico!.ListarFiliaisAsync();
        Filiais.Clear();
        foreach (var f in filiais) Filiais.Add(f);
        SemFiliais = Filiais.Count == 0;
        FilialSelecionada = Filiais.FirstOrDefault();
    }

    private async Task RecarregarAsync(bool refazerSetores = false)
    {
        Dias.Clear();
        if (_servico is null || FilialSelecionada is null) return;

        var semana = await _servico.ObterSemanaAsync(FilialSelecionada.Id, _inicioSemana);
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
        foreach (var d in semana.Dias) Dias.Add(new DiaViewModel(d, SetorSelecionado?.Id));
    }

    private void AtualizarTitulo()
    {
        var fim = _inicioSemana.AddDays(6);
        TituloSemana = $"{_inicioSemana.Day} {Meses[_inicioSemana.Month - 1]} – {fim.Day} {Meses[fim.Month - 1]} {fim.Year}";
    }

    private async Task MudarSemanaAsync(DateOnly inicio)
    {
        _inicioSemana = inicio;
        AtualizarTitulo();
        Mensagem = null;
        await RecarregarAsync();
    }

    [RelayCommand]
    private Task SemanaAnteriorAsync() => MudarSemanaAsync(_inicioSemana.AddDays(-7));

    [RelayCommand]
    private Task ProximaSemanaAsync() => MudarSemanaAsync(_inicioSemana.AddDays(7));

    [RelayCommand]
    private Task IrParaHojeAsync() => MudarSemanaAsync(RegraEscala.InicioDaSemana(DateOnly.FromDateTime(DateTime.Today)));

    [RelayCommand]
    private async Task AlternarDiaAsync(DiaViewModel dia)
    {
        if (_servico is null || FilialSelecionada is null) return;
        await Aplicar(_servico.DefinirDiaFechadoAsync(FilialSelecionada.Id, dia.Data, !dia.Fechado));
    }

    /// <summary>Chamado pela View ao soltar um funcionário sobre um dia.</summary>
    public Task SoltarEmDiaAsync(int funcionarioId, DateOnly? origem, DateOnly destino)
    {
        if (_servico is null) return Task.CompletedTask;
        if (origem is null) return Aplicar(_servico.EscalarAsync(funcionarioId, destino));
        if (origem == destino) return Task.CompletedTask;
        return Aplicar(_servico.MoverAsync(funcionarioId, origem.Value, destino));
    }

    /// <summary>Chamado pela View ao soltar um funcionário escalado na lista de disponíveis.</summary>
    public Task SoltarEmDisponiveisAsync(int funcionarioId, DateOnly? origem)
    {
        if (_servico is null || origem is null) return Task.CompletedTask;
        return Aplicar(_servico.RemoverAsync(funcionarioId, origem.Value));
    }

    private async Task Aplicar(Task<ResultadoOperacao> operacao)
    {
        var r = await operacao;
        Mensagem = r.Ok ? null : r.Erro;
        await RecarregarAsync();
    }
}
