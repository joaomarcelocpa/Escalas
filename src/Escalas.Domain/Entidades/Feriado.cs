namespace Escalas.Domain.Entidades;

public enum AbrangenciaFeriado { Nacional = 1, Estadual = 2, Municipal = 3 }

public class Feriado
{
    public int Id { get; set; }
    public DateOnly Data { get; set; }
    public string Nome { get; set; } = "";
    public AbrangenciaFeriado Abrangencia { get; set; }
    public string? UF { get; set; }
    public string? Cidade { get; set; }
    public int? FilialId { get; set; }
    public Filial? Filial { get; set; }
}
