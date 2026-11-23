namespace Escalas.Domain.Calendario;

/// <summary>Regras de calendário da escala.</summary>
public static class RegraEscala
{
    public static DateOnly InicioDaSemana(DateOnly data)
    {
        var delta = ((int)data.DayOfWeek + 6) % 7; // segunda = 0
        return data.AddDays(-delta);
    }
}
