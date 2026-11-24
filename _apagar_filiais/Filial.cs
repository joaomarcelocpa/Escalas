namespace Escalas.Domain.Entidades;

public class Filial
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string UF { get; set; } = "";
    public List<Setor> Setores { get; set; } = [];
}
