namespace Escalas.Domain.Entidades;

/// <summary>
/// Exceção pontual ao modelo semanal (<see cref="EscalaPadrao"/>) em uma data:
/// <see cref="Removido"/> = false inclui o funcionário naquele dia; true o retira do dia mesmo que o modelo o inclua.
/// </summary>
public class Escala
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
    public DateOnly Data { get; set; }
    public bool Removido { get; set; }
}
