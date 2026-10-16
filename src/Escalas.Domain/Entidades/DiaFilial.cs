namespace Escalas.Domain.Entidades;

public class DiaFilial
{
    public int Id { get; set; }
    public int FilialId { get; set; }
    public Filial? Filial { get; set; }
    public DateOnly Data { get; set; }
    public bool Fechado { get; set; }
}
