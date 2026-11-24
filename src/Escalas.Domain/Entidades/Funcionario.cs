namespace Escalas.Domain.Entidades;

public class Funcionario
{
    public int Id { get; set; }
    public Filial Filial { get; set; }
    /// <summary>
    /// Filial extra (opcional) em que o funcionário pode cobrir a folga dos funcionários fixos de lá.
    /// Nunca é a própria <see cref="Filial"/>. A cobertura em si fica no modelo semanal (<see cref="EscalaPadrao.FilialCobertura"/>).
    /// </summary>
    public Filial? FilialExtra { get; set; }
    public int SetorId { get; set; }
    public Setor? Setor { get; set; }
    public string Nome { get; set; } = "";
    public bool Ativo { get; set; } = true;
    /// <summary>Foto já redimensionada (PNG pequeno), guardada no próprio banco.</summary>
    public byte[]? Foto { get; set; }
}
