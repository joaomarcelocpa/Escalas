namespace Escalas.Domain.Entidades;

/// <summary>Um registro por funcionário escalado em um dia.</summary>
public class Escala
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
    public DateOnly Data { get; set; }
}
