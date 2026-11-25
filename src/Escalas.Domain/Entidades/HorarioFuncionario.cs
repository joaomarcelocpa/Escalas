namespace Escalas.Domain.Entidades;

/// <summary>
/// Horário de trabalho próprio de um funcionário em um dia da semana (vale para todas as semanas).
/// Só é usado quando <see cref="Funcionario.HorarioPersonalizado"/> está ativo; nos dias sem linha aqui vale o
/// expediente geral do dia. Não define folga: quem trabalha em cada dia continua sendo o modelo da escala semanal.
/// </summary>
public class HorarioFuncionario
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public DayOfWeek DiaSemana { get; set; }
    public TimeOnly Inicio { get; set; }
    public TimeOnly Fim { get; set; }
}
