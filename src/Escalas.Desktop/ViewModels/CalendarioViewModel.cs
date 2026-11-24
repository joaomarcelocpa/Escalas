using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Calendario;
using System.Globalization;
using Escalas.Desktop.Servicos;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

/// <summary>Funcionário exibido em um dia (somente leitura). <see cref="Cobertura"/>: veio de outra filial cobrir folga.</summary>
public record FuncionarioItem(int Id, string Nome, string? Detalhe, byte[]? Foto = null, bool Cobertura = false);

/// <summary>Opção do filtro de setor; <see cref="Id"/> nulo significa todos os setores.</summary>
public record SetorFiltro(int? Id, string Nome);

public class DiaViewModel
{
    private static readonly string[] DiasSemana = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

    public DiaViewModel(DiaDto dto, ExpedienteDto? expediente = null)
    {
        Data = dto.Data;
        DiaSemana = DiasSemana[(int)dto.Data.DayOfWeek];
        Numero = dto.Data.Day.ToString("00");
        NomeFeriado = dto.NomeFeriado;
        Fechado = dto.Fechado;
        TextoFechar = dto.Fechado ? "Reabrir" : "Fechar";
        // Quem veio cobrir folga entra na lista do dia, marcado como cobertura.
        Escalados = dto.Setores
            .SelectMany(s => s.Escalados.Select(f => new FuncionarioItem(f.Id, f.Nome, s.Nome, f.Foto)))
            .Concat(dto.Cobrindo.Select(c => new FuncionarioItem(c.Id, c.Nome, $"Cobertura · {Filiais.Nome(c.Filial)}", c.Foto, true)))
            .OrderBy(f => f.Nome).ToList();
        // Quem saiu daqui para cobrir outra filial não está de folga: aparece à parte.
        Fora = dto.Fora.Select(c => new FuncionarioItem(c.Id, c.Nome, $"Cobre {Filiais.Nome(c.Filial)}", c.Foto)).ToList();
        TextoFora = $"Em outra filial ({Fora.Count})";
        Hoje = dto.Data == DateOnly.FromDateTime(DateTime.Today);
        Disponiveis = dto.Disponiveis.Select(f => new FuncionarioItem(f.Id, f.Nome, f.SetorNome, f.Foto)).ToList();
        TextoDisponiveis = $"Folga ({Disponiveis.Count})";
        SemExpediente = expediente is null;
        TextoExpediente = expediente is null
            ? "Sem expediente"
            : RegraHoras.FormatarIntervalo(expediente.Inicio, expediente.Fim);
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
    /// <summary>Funcionários desta filial cobrindo folga em outra filial neste dia.</summary>
    public IReadOnlyList<FuncionarioItem> Fora { get; }
    public string TextoFora { get; }
    public bool TemFora => Fora.Count > 0;
    /// <summary>Expediente do dia da semana ("08:00–17:00") ou "Sem expediente". Só exibição: a edição fica na escala semanal.</summary>
    public string TextoExpediente { get; }
    public bool SemExpediente { get; }
    public bool MostrarExpediente => !Fechado;
}

public partial class CalendarioViewModel : ViewModelBase
{
    private static readonly string[] Meses =
        ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly IEscalaService? _servico;
    private readonly IDialogoConfirmacao? _dialogo;
    private readonly IExpedienteService? _expedientes;
    private readonly IDialogosHorario? _dialogosHorario;
    private DateOnly _inicioSemana = RegraEscala.InicioDaSemana(DateOnly.FromDateTime(DateTime.Today));

    /// <summary>Filiais fixas.</summary>
    public IReadOnlyList<FilialOpcao> Filiais { get; } = global::Escalas.Domain.Entidades.Filiais.Todas;
    public ObservableCollection<DiaViewModel> Dias { get; } = [];
    [ObservableProperty]
    public partial FilialOpcao? FilialSelecionada { get; set; }

    [ObservableProperty]
    public partial string TituloSemana { get; set; } = "";

    [ObservableProperty]
    public partial string? Mensagem { get; set; }

    /// <summary>Construtor para o previewer.</summary>
    public CalendarioViewModel() => AtualizarTitulo();

    public CalendarioViewModel(IEscalaService servico, IDialogoConfirmacao dialogo,
        IExpedienteService expedientes, IDialogosHorario dialogosHorario)
    {
        _servico = servico;
        _dialogo = dialogo;
        _expedientes = expedientes;
        _dialogosHorario = dialogosHorario;
        AtualizarTitulo();
        FilialSelecionada = Filiais[0];
    }

    partial void OnFilialSelecionadaChanged(FilialOpcao? value) => _ = RecarregarAsync();

    private async Task RecarregarAsync()
    {
        if (_servico is null || FilialSelecionada is null) { Dias.Clear(); return; }

        var semana = await _servico.ObterSemanaAsync(FilialSelecionada.Id, _inicioSemana);
        var expedientes = _expedientes is null
            ? new Dictionary<DayOfWeek, ExpedienteDto>()
            : (await _expedientes.ObterAsync()).ToDictionary(e => e.Dia);
        // Troca tudo de uma vez, só depois da consulta: evita o piscar de tela vazia ao mudar de semana.
        var novos = semana.Dias.Select(d => new DiaViewModel(d, expedientes.GetValueOrDefault(d.Data.DayOfWeek))).ToList();
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

    /// <summary>Popup com os dias e as horas do funcionário na semana visível.</summary>
    [RelayCommand]
    private Task VerHorasAsync(FuncionarioItem funcionario) =>
        FilialSelecionada is null
            ? Task.CompletedTask
            : _dialogosHorario?.MostrarHorasFuncionarioAsync(funcionario.Id, _inicioSemana, FilialSelecionada.Id) ?? Task.CompletedTask;

    /// <summary>Resumo da semana visível: todos os funcionários, por filial, com dias, horários e horas.</summary>
    [RelayCommand]
    private Task ResumoSemanalAsync() =>
        _dialogosHorario?.MostrarResumoSemanalAsync(_inicioSemana) ?? Task.CompletedTask;

    private static string Texto(DateOnly data) => data.ToString("ddd dd/MM", PtBr);

    private async Task<bool> Confirmar(string titulo, string mensagem, string textoConfirmar)
    {
        Mensagem = null;
        return _dialogo is null || await _dialogo.ConfirmarAsync(titulo, mensagem + "\n\nVale apenas para este dia; o modelo semanal não muda.", textoConfirmar);
    }

    private async Task Aplicar(Task<ResultadoOperacao> operacao)
    {
        var r = await operacao;
        Mensagem = r.Ok ? null : r.Erro;
        await RecarregarAsync();
    }
}
