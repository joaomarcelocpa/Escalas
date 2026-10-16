namespace Escalas.Domain.Entidades;

/// <summary>Sempre uma única linha por banco.</summary>
public class Empresa
{
    public int Id { get; set; } = 1;
    public string Nome { get; set; } = "";
    public string Documento { get; set; } = "";
}
