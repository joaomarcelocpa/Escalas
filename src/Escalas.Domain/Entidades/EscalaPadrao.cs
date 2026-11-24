namespace Escalas.Domain.Entidades;

/// <summary>Modelo semanal: o funcionário trabalha neste dia da semana, em todas as semanas (segunda a sábado).</summary>
public class EscalaPadrao
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
    public DayOfWeek DiaSemana { get; set; }
    /// <summary>
    /// Nulo: o funcionário trabalha na própria filial neste dia. Preenchido: neste dia ele sai da filial dele e cobre
    /// esta filial (sempre a <see cref="Funcionario.FilialExtra"/>). Como há uma linha por funcionário e dia,
    /// ninguém fica em duas filiais no mesmo dia.
    /// </summary>
    public Filial? FilialCobertura { get; set; }
}
