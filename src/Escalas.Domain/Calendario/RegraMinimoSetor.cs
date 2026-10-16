namespace Escalas.Domain.Calendario;

public record SetorMinimo(int SetorId, string Nome, int Minimo);

public record AlertaMinimo(int SetorId, string Nome, int Minimo, int Escalados);

/// <summary>Indica quais setores ficam abaixo do mínimo em um dia.</summary>
public static class RegraMinimoSetor
{
    /// <param name="escaladosPorSetor">Quantidade de funcionários escalados por setor no dia (setor ausente = 0).</param>
    /// <param name="diaFechado">Dia fechado nunca gera alerta.</param>
    public static IReadOnlyList<AlertaMinimo> Avaliar(
        IEnumerable<SetorMinimo> setores,
        IReadOnlyDictionary<int, int> escaladosPorSetor,
        bool diaFechado)
    {
        if (diaFechado) return [];

        return setores
            .Where(s => s.Minimo > 0)
            .Select(s => new AlertaMinimo(s.SetorId, s.Nome, s.Minimo, escaladosPorSetor.GetValueOrDefault(s.SetorId)))
            .Where(a => a.Escalados < a.Minimo)
            .ToList();
    }
}
