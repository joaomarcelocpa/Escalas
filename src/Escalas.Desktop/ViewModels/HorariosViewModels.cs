using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Calendario;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;
using FiliaisFixas = Escalas.Domain.Entidades.Filiais;

namespace Escalas.Desktop.ViewModels;

/// <summary>Textos comuns da gestão de horários.</summary>
internal static class TextoHorario
{
    public static readonly string[] DiasCurtos = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
    public static readonly string[] DiasLongos = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];
    private static readonly string[] Meses = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    public static string Data(DateOnly d) => d.ToString("dd'/'MM", CultureInfo.InvariantCulture);

    public static string Semana(DateOnly inicio)
    {
        var fim = inicio.AddDays(6);
        return $"{inicio.Day} {Meses[inicio.Month - 1]} – {fim.Day} {Meses[fim.Month - 1]} {fim.Year}";
    }

    public static string Hora(TimeOnly h) => h.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static readonly string Almoco = $"−{RegraHoras.Formatar(RegraHoras.Almoco)} almoço";

    public static readonly string RegraAlmoco =
        $"Dias com {RegraHoras.Formatar(RegraHoras.MinimoParaAlmoco)} ou mais de expediente descontam {RegraHoras.Formatar(RegraHoras.Almoco)} de almoço.";

    public static string? AvisoSemExpediente(int dias) => dias switch
    {
        0 => null,
        1 => "1 dia escalado está sem expediente cadastrado e não entra no total.",
        _ => $"{dias} dias escalados estão sem expediente cadastrado e não entram no total.",
    };
}

// ───────────── Expediente por dia da semana (edição na escala semanal) ─────────────

/// <summary>Uma linha do diálogo de expediente: horas digitadas como texto ("08:00").</summary>
public partial class ExpedienteDiaItem : ObservableObject
{
    public ExpedienteDiaItem(DayOfWeek dia, ExpedienteDto? atual)
    {
        Dia = dia;
        Titulo = TextoHorario.DiasLongos[(int)dia];
        Inicio = atual is null ? "" : TextoHorario.Hora(atual.Inicio);
        Fim = atual is null ? "" : TextoHorario.Hora(atual.Fim);
    }

    public DayOfWeek Dia { get; }
    public string Titulo { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duracao))]
    public partial string Inicio { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duracao))]
    public partial string Fim { get; set; }

    /// <summary>Prévia das horas do dia, ou orientação quando incompleto/inválido.</summary>
    public string Duracao => Ler(out var entrada, out var erro)
        ? entrada.Inicio is { } i && entrada.Fim is { } f ? RegraHoras.FormatarHorasDoDia(i, f) : "Sem expediente"
        : erro!;

    public void Limpar() => (Inicio, Fim) = ("", "");

    /// <summary>Converte o que foi digitado. Os dois vazios = sem expediente.</summary>
    public bool Ler(out ExpedienteEntrada entrada, out string? erro)
    {
        entrada = new ExpedienteEntrada(Dia, null, null);
        erro = null;
        var vazioInicio = string.IsNullOrWhiteSpace(Inicio);
        var vazioFim = string.IsNullOrWhiteSpace(Fim);
        if (vazioInicio && vazioFim) return true;
        if (vazioInicio || vazioFim) { erro = "Preencha início e fim"; return false; }
        if (!RegraHoras.TentarLerHora(Inicio, out var i) || !RegraHoras.TentarLerHora(Fim, out var f))
        {
            erro = "Hora inválida";
            return false;
        }
        if (RegraHoras.Validar(i, f) is not null) { erro = "Fim antes do início"; return false; }
        entrada = new ExpedienteEntrada(Dia, i, f);
        return true;
    }
}

/// <summary>Edita o expediente de segunda a sábado (mesmos dias da escala semanal); vale para todas as semanas.</summary>
public partial class ExpedienteSemanaViewModel : ViewModelBase
{
    public static readonly DayOfWeek[] DiasEditaveis =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    private readonly IExpedienteService? _servico;

    /// <summary>Disparado ao fechar: true se salvou.</summary>
    public event Action<bool>? Concluido;

    public ExpedienteSemanaViewModel() : this(null) { }

    public ExpedienteSemanaViewModel(IExpedienteService? servico)
    {
        _servico = servico;
        foreach (var d in DiasEditaveis) Dias.Add(new ExpedienteDiaItem(d, null));
    }

    public ObservableCollection<ExpedienteDiaItem> Dias { get; } = [];

    [ObservableProperty] public partial string? Erro { get; set; }

    public async Task CarregarAsync()
    {
        if (_servico is null) return;
        var atuais = (await _servico.ObterAsync()).ToDictionary(e => e.Dia);
        Dias.Clear();
        foreach (var d in DiasEditaveis) Dias.Add(new ExpedienteDiaItem(d, atuais.GetValueOrDefault(d)));
    }

    /// <summary>Repete o horário do primeiro dia preenchido nos demais dias.</summary>
    [RelayCommand]
    private void RepetirPrimeiro()
    {
        var modelo = Dias.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Inicio) || !string.IsNullOrWhiteSpace(d.Fim));
        if (modelo is null) { Erro = "Preencha um dia para repetir."; return; }
        foreach (var d in Dias.Where(d => d != modelo))
            (d.Inicio, d.Fim) = (modelo.Inicio, modelo.Fim);
        Erro = null;
    }

    [RelayCommand]
    private void LimparDia(ExpedienteDiaItem dia) => dia.Limpar();

    [RelayCommand]
    private async Task SalvarAsync()
    {
        var entradas = new List<ExpedienteEntrada>();
        foreach (var d in Dias)
        {
            if (!d.Ler(out var e, out var erro)) { Erro = $"{d.Titulo}: {erro}."; return; }
            entradas.Add(e);
        }
        if (_servico is not null)
        {
            var r = await _servico.SalvarAsync(entradas);
            if (!r.Ok) { Erro = r.Erro; return; }
        }
        Concluido?.Invoke(true);
    }

    [RelayCommand]
    private void Cancelar() => Concluido?.Invoke(false);
}

// ───────────── Horas de um funcionário (popup do calendário) ─────────────

/// <param name="Intervalo">"08:00–17:00" ou "Sem expediente".</param>
/// <param name="Almoco">"−1h30 almoço" quando o almoço foi descontado; vazio caso contrário.</param>
/// <param name="Local">"Cobre Interiores" no dia de cobertura de folga; vazio na própria filial.</param>
public record HorasDiaItem(string Dia, string Data, string Intervalo, string Horas, bool SemExpediente, bool Futuro, string Almoco = "", string Local = "")
{
    public bool TemAlmoco => Almoco.Length > 0;
    public bool TemLocal => Local.Length > 0;
}

public class HorasFuncionarioViewModel : ViewModelBase
{
    public HorasFuncionarioViewModel() : this(new HorasFuncionarioDto(0, "Funcionário", "Setor", Filial.Matriz,
        RegraEscala.InicioDaSemana(DateOnly.FromDateTime(DateTime.Today)), [], new TotalHoras(0, TimeSpan.Zero, TimeSpan.Zero, 0))) { }

    public HorasFuncionarioViewModel(HorasFuncionarioDto dto) : this(dto, DateOnly.FromDateTime(DateTime.Today)) { }

    public HorasFuncionarioViewModel(HorasFuncionarioDto dto, DateOnly hoje)
    {
        Nome = dto.Nome;
        Foto = dto.Foto;
        Detalhe = string.IsNullOrEmpty(dto.Setor) ? Filiais.Nome(dto.Filial) : $"{dto.Setor} · {Filiais.Nome(dto.Filial)}";
        Semana = TextoHorario.Semana(dto.InicioSemana);
        Dias = dto.Dias.Select(d => new HorasDiaItem(
            TextoHorario.DiasLongos[(int)d.Data.DayOfWeek],
            TextoHorario.Data(d.Data),
            d.TemExpediente ? RegraHoras.FormatarIntervalo(d.Inicio!.Value, d.Fim!.Value) : "Sem expediente",
            d.Horas is { } h ? RegraHoras.Formatar(h) : "—",
            !d.TemExpediente,
            d.Data > hoje,
            d.DescontaAlmoco ? TextoHorario.Almoco : "",
            d.Cobertura is { } c ? $"Cobre {Filiais.Nome(c)}" : "")).ToList();
        TotalSemana = RegraHoras.Formatar(dto.Totais.Total);
        TotalAteHoje = RegraHoras.Formatar(dto.Totais.AteHoje);
        TextoDias = dto.Totais.Dias == 1 ? "1 dia escalado" : $"{dto.Totais.Dias} dias escalados";
        Aviso = TextoHorario.AvisoSemExpediente(dto.Totais.DiasSemExpediente);
    }

    public string Nome { get; }
    public byte[]? Foto { get; }
    public string Detalhe { get; }
    public string Semana { get; }
    public IReadOnlyList<HorasDiaItem> Dias { get; }
    public bool SemDias => Dias.Count == 0;
    public string TextoDias { get; }
    public string TotalSemana { get; }
    public string TotalAteHoje { get; }
    public string? Aviso { get; }
    public bool TemAviso => Aviso is not null;
}

// ───────────── Resumo semanal de todos, por filial ─────────────

/// <param name="Trabalha">Escalado no dia.</param>
/// <param name="Horas">"8h" (já sem o almoço) ou "—".</param>
/// <param name="Local">Filial coberta no dia (cobertura de folga), na linha da filial do funcionário; vazio caso contrário.</param>
public record ResumoCelulaItem(bool Trabalha, string Intervalo, string Horas, bool SemExpediente, string Local = "")
{
    public bool TemLocal => Local.Length > 0;
}

public record ResumoLinhaItem(string Nome, string Setor, IReadOnlyList<ResumoCelulaItem> Celulas, string Dias, string Total, byte[]? Foto = null);

public record ResumoFilialItem(string Nome, IReadOnlyList<ResumoLinhaItem> Linhas)
{
    public bool Vazia => Linhas.Count == 0;
}

public class ResumoSemanalViewModel : ViewModelBase
{
    public ResumoSemanalViewModel() : this(new ResumoSemanalDto(
        RegraEscala.InicioDaSemana(DateOnly.FromDateTime(DateTime.Today)), [], [])) { }

    public ResumoSemanalViewModel(ResumoSemanalDto dto)
    {
        Semana = TextoHorario.Semana(dto.Inicio);
        var datas = Enumerable.Range(0, 6).Select(i => dto.Inicio.AddDays(i)).ToList(); // segunda a sábado (sem domingo)
        Cabecalho = datas.Select(d => $"{TextoHorario.DiasCurtos[(int)d.DayOfWeek]} {TextoHorario.Data(d)}").ToList();
        Filiais = dto.Filiais.Select(f => new ResumoFilialItem(
            f.Nome,
            f.Funcionarios.Select(func =>
            {
                var porData = func.Dias.ToDictionary(d => d.Data);
                var celulas = datas.Select(d =>
                {
                    if (!porData.TryGetValue(d, out var dia))
                        return new ResumoCelulaItem(false, "Folga", "", false);
                    var local = dia.Cobertura is { } c ? $"Cobre {FiliaisFixas.Nome(c)}" : "";
                    return dia.TemExpediente
                        ? new ResumoCelulaItem(true, RegraHoras.FormatarIntervalo(dia.Inicio!.Value, dia.Fim!.Value), RegraHoras.Formatar(dia.Horas!.Value), false, local)
                        : new ResumoCelulaItem(true, "Sem expediente", "—", true, local);
                }).ToList();
                var setor = func.Setor;
                return new ResumoLinhaItem(func.Nome, setor, celulas,
                    func.Totais.Dias == 1 ? "1 dia" : $"{func.Totais.Dias} dias",
                    RegraHoras.Formatar(func.Totais.Total), func.Foto);
            }).ToList())).ToList();

        Aviso = dto.DiasSemExpediente.Count == 0
            ? null
            : "Sem expediente cadastrado: " + string.Join(", ", dto.DiasSemExpediente.Select(d =>
                $"{TextoHorario.DiasCurtos[(int)d.DayOfWeek]} {TextoHorario.Data(d)}")) + ". Escalados nesses dias não somam horas.";
    }

    public string Semana { get; }
    public IReadOnlyList<string> Cabecalho { get; }
    public IReadOnlyList<ResumoFilialItem> Filiais { get; }
    public string? Aviso { get; }
    public bool TemAviso => Aviso is not null;
    public string RegraAlmoco => TextoHorario.RegraAlmoco;
}
