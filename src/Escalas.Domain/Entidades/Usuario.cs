namespace Escalas.Domain.Entidades;

public enum PapelUsuario { Administrador = 1, Comum = 2 }

public class Usuario
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public PapelUsuario Papel { get; set; } = PapelUsuario.Comum;
}
