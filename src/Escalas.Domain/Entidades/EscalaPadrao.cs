namespace Escalas.Domain.Entidades;

/// <summary>Modelo semanal: o funcionário trabalha neste dia da semana, em todas as semanas (segunda a sábado).</summary>
public class EscalaPadrao
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
    public DayOfWeek DiaSemana { get; set; }
}
