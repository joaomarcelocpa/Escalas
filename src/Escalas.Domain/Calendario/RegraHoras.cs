using System.Globalization;
using System.Text.RegularExpressions;
using Escalas.Domain.Entidades;

namespace Escalas.Domain.Calendario;

/// <summary>Dia em que o funcionário está escalado, com o expediente daquela data (nulo se não cadastrado).</summary>
/// <param name="Cobertura">Filial coberta neste dia (folga de outra filial); nulo quando trabalha na própria filial.</param>
public record DiaEscalado(DateOnly Data, TimeOnly? Inicio, TimeOnly? Fim, Filial? Cobertura = null)
{
    public bool TemExpediente => Inicio is not null && Fim is not null;

    /// <summary>Horas trabalhadas no dia (expediente menos o almoço, quando houver); nulo se a data não tem expediente.</summary>
    public TimeSpan? Horas => TemExpediente ? RegraHoras.HorasTrabalhadas(Inicio!.Value, Fim!.Value) : null;

    /// <summary>O almoço fixo foi descontado neste dia (expediente de 8h ou mais).</summary>
    public bool DescontaAlmoco => TemExpediente && RegraHoras.DescontaAlmoco(Inicio!.Value, Fim!.Value);
}

/// <param name="Total">Soma das horas de todos os dias escalados da semana que têm expediente.</param>
/// <param name="AteHoje">Mesma soma, só com os dias até hoje (inclusive): o que o funcionário já trabalhou.</param>
/// <param name="DiasSemExpediente">Dias escalados cuja data não tem expediente cadastrado (não entram nas horas).</param>
public record TotalHoras(int Dias, TimeSpan Total, TimeSpan AteHoje, int DiasSemExpediente);

/// <summary>Cálculo de horas a partir do expediente geral do dia.</summary>
public static class RegraHoras
{
    /// <summary>Almoço fixo, igual para todos os funcionários.</summary>
    public static readonly TimeSpan Almoco = TimeSpan.FromMinutes(90);

    /// <summary>Expediente a partir do qual o almoço é descontado.</summary>
    public static readonly TimeSpan MinimoParaAlmoco = TimeSpan.FromHours(8);

    /// <summary>Duração do expediente. Não atravessa a meia-noite: fim deve ser depois do início.</summary>
    public static TimeSpan Duracao(TimeOnly inicio, TimeOnly fim) =>
        fim > inicio ? fim - inicio : throw new ArgumentException("O fim do expediente deve ser depois do início.");

    /// <summary>Dias com 8h ou mais de expediente descontam o almoço.</summary>
    public static bool DescontaAlmoco(TimeOnly inicio, TimeOnly fim) => Duracao(inicio, fim) >= MinimoParaAlmoco;

    /// <summary>Horas trabalhadas no dia: duração do expediente, menos 1h30 de almoço se o expediente tiver 8h ou mais.</summary>
    public static TimeSpan HorasTrabalhadas(TimeOnly inicio, TimeOnly fim)
    {
        var duracao = Duracao(inicio, fim);
        return duracao >= MinimoParaAlmoco ? duracao - Almoco : duracao;
    }

    /// <summary>"8h" ou "8h (−1h30 almoço)".</summary>
    public static string FormatarHorasDoDia(TimeOnly inicio, TimeOnly fim) =>
        DescontaAlmoco(inicio, fim)
            ? $"{Formatar(HorasTrabalhadas(inicio, fim))} (−{Formatar(Almoco)} almoço)"
            : Formatar(HorasTrabalhadas(inicio, fim));

    /// <summary>Erro de validação do expediente, ou null se válido.</summary>
    public static string? Validar(TimeOnly inicio, TimeOnly fim) =>
        fim > inicio ? null : "O fim do expediente deve ser depois do início.";

    public static TotalHoras Totalizar(IEnumerable<DiaEscalado> dias, DateOnly hoje)
    {
        var lista = dias.ToList();
        var total = TimeSpan.Zero;
        var ateHoje = TimeSpan.Zero;
        foreach (var d in lista)
        {
            if (d.Horas is not { } h) continue;
            total += h;
            if (d.Data <= hoje) ateHoje += h;
        }
        return new TotalHoras(lista.Count, total, ateHoje, lista.Count(d => !d.TemExpediente));
    }

    /// <summary>Formata horas como "8h", "8h30", "44h05". Zero vira "0h".</summary>
    public static string Formatar(TimeSpan horas)
    {
        var minutos = (int)Math.Round(horas.TotalMinutes);
        var h = minutos / 60;
        var m = minutos % 60;
        return m == 0 ? $"{h}h" : $"{h}h{m:00}";
    }

    /// <summary>"08:00–17:00".</summary>
    public static string FormatarIntervalo(TimeOnly inicio, TimeOnly fim) =>
        $"{inicio.ToString("HH:mm", CultureInfo.InvariantCulture)}–{fim.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    private static readonly Regex PadraoHora = new(@"^(\d{1,2})(?:[:h]?(\d{2}))?h?$", RegexOptions.CultureInvariant);

    /// <summary>Lê horas digitadas como "8", "08:00", "8:30", "0830", "8h30" ou "18h".</summary>
    public static bool TentarLerHora(string? texto, out TimeOnly hora)
    {
        hora = default;
        var m = PadraoHora.Match((texto ?? "").Trim().ToLowerInvariant());
        if (!m.Success) return false;
        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (h > 23 || min > 59) return false;
        hora = new TimeOnly(h, min);
        return true;
    }
}
