namespace Escalas.Domain.Entidades;

public class Setor
{
    public int Id { get; set; }
    public Filial Filial { get; set; }
    public string Nome { get; set; } = "";
    public int MinimoFuncionarios { get; set; }
}
