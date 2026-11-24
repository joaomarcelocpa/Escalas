namespace Escalas.Domain.Entidades;

/// <summary>
/// Horário geral de funcionamento de um dia da semana, definido na escala semanal e repetido em todas as semanas.
/// Vale para todas as filiais e para todos os funcionários escalados no dia. É a base do cálculo de horas (Fim − Início).
/// Dia da semana sem expediente não soma horas.
/// </summary>
public class Expediente
{
    public int Id { get; set; }
    public DayOfWeek DiaSemana { get; set; }
    public TimeOnly Inicio { get; set; }
    public TimeOnly Fim { get; set; }
}
