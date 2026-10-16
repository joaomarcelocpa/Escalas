using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Calendario;
using System.Globalization;
using Escalas.Desktop.Servicos;
using Escalas.Domain.Calendario;

namespace Escalas.Desktop.ViewModels;

/// <summary>Funcionário exibido em um dia. <see cref="Origem"/> é nulo quando vem da lista de disponíveis.</summary>
public record FuncionarioItem(int Id, string Nome, string? Detalhe, DateOnly? Origem, byte[]? Foto = null);

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
        Escalados = dto.Setores.Where(s => setorId is null || s.SetorId == setorId)
            .SelectMany(s => s.Escalados.Select(f => new FuncionarioItem(f.Id, f.Nome, s.Nome, dto.Data, f.Foto)))
            .OrderBy(f => f.Nome).ToList();
        Hoje = dto.Data == DateOnly.FromDateTime(DateTime.Today);
        Disponiveis = dto.Disponiveis.Where(f => setorId is null || f.SetorId == setorId).Select(f => new FuncionarioItem(f.Id, f.Nome, f.SetorNome, null, f.Foto)).ToList();
        TextoDisponiveis = $"Folga ({Disponiveis.Count})";
    }

    public DateOnly Data { get; }
    public string DiaSemana { get; }
    public string Numero { get; }
    public string? NomeFeriado { get; }
    public bool TemFeriado => NomeFeriado is not null;
    public bool Fechado { get; }
    public string TextoFechar { get; }
    public IReadOnlyList<FuncionarioItem> Escalados { get; }
    public bool Hoje { get; }
    public IReadOnlyList<FuncionarioItem> Disponiveis { get; }
    public string TextoDisponiveis { get; }
}

public partial class CalendarioViewModel : ViewModelBase
{
    private static readonly string[] Meses =
        ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly IEscalaService? _servico;
    private readonly IDialogoConfirmacao? _dialogo;
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

    public CalendarioViewModel(IEscalaService servico, IDialogoConfirmacao dialogo)
    {
        _servico = servico;
        _dialogo = dialogo;
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
        if (_servico is null || FilialSelecionada is null) { Dias.Clear(); return; }

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
        // Troca tudo de uma vez, só depois da consulta: evita o piscar de tela vazia ao mudar de semana.
        var novos = semana.Dias.Select(d => new DiaViewModel(d, SetorSelecionado?.Id)).ToList();
        Dias.Clear();
        foreach (var d in novos) Dias.Add(d);
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
        var fechar = !dia.Fechado;
        var mensagem = fechar
            ? $"Fechar {Texto(dia.Data)}? Ninguém ficará escalado nesse dia."
            : $"Reabrir {Texto(dia.Data)}? A escala volta a valer nesse dia.";
        if (!await Confirmar(fechar ? "Fechar dia" : "Reabrir dia", mensagem, fechar ? "Fechar" : "Reabrir")) return;
        await Aplicar(_servico.DefinirDiaFechadoAsync(FilialSelecionada.Id, dia.Data, fechar));
    }

    [RelayCommand]
    private async Task RemoverEscaladoAsync(FuncionarioItem item)
    {
        if (item.Origem is { } data) await SoltarEmDisponiveisAsync(item.Id, data);
    }

    private static string Texto(DateOnly data) => data.ToString("ddd dd/MM", PtBr);

    private string NomeDe(int funcionarioId) =>
        Dias.SelectMany(d => d.Escalados.Concat(d.Disponiveis))
            .FirstOrDefault(f => f.Id == funcionarioId)?.Nome ?? "o funcionário";

    private async Task<bool> Confirmar(string titulo, string mensagem, string textoConfirmar)
    {
        Mensagem = null;
        return _dialogo is null || await _dialogo.ConfirmarAsync(titulo, mensagem + "\n\nVale apenas para este dia; o modelo semanal não muda.", textoConfirmar);
    }

    /// <summary>Chamado pela View ao soltar um funcionário sobre um dia.</summary>
    public async Task SoltarEmDiaAsync(int funcionarioId, DateOnly? origem, DateOnly destino)
    {
        if (_servico is null) return;
        var nome = NomeDe(funcionarioId);
        if (origem is null)
        {
            if (!await Confirmar("Incluir no dia", $"Incluir {nome} em {Texto(destino)}?", "Incluir")) return;
            await Aplicar(_servico.EscalarAsync(funcionarioId, destino));
            return;
        }
        if (origem == destino) return;
        if (!await Confirmar("Mover funcionário", $"Mover {nome} de {Texto(origem.Value)} para {Texto(destino)}?", "Mover")) return;
        await Aplicar(_servico.MoverAsync(funcionarioId, origem.Value, destino));
    }

    /// <summary>Chamado pela View ao soltar um funcionário escalado na lista de disponíveis.</summary>
    public async Task SoltarEmDisponiveisAsync(int funcionarioId, DateOnly? origem)
    {
        if (_servico is null || origem is null) return;
        if (!await Confirmar("Remover do dia", $"Remover {NomeDe(funcionarioId)} de {Texto(origem.Value)}?", "Remover")) return;
        await Aplicar(_servico.RemoverAsync(funcionarioId, origem.Value));
    }

    private async Task Aplicar(Task<ResultadoOperacao> operacao)
    {
        var r = await operacao;
        Mensagem = r.Ok ? null : r.Erro;
        await RecarregarAsync();
    }
}
