using Escalas.Domain.Entidades;

namespace Escalas.Domain.Calendario;

/// <summary>Início e fim do trabalho em um dia.</summary>
public record HorarioDia(TimeOnly Inicio, TimeOnly Fim);

/// <summary>
/// Horário que vale para cada funcionário em cada dia da semana: o personalizado do funcionário, quando ele tem
/// horário personalizado ativo e definiu aquele dia; senão, o expediente geral do dia (ou nenhum).
/// </summary>
public sealed class TabelaHorarios(
    IReadOnlyDictionary<DayOfWeek, HorarioDia> geral,
    IReadOnlyDictionary<int, IReadOnlyDictionary<DayOfWeek, HorarioDia>>? personalizados = null)
{
    private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<DayOfWeek, HorarioDia>> _personalizados =
        personalizados ?? new Dictionary<int, IReadOnlyDictionary<DayOfWeek, HorarioDia>>();

    public static readonly TabelaHorarios Vazia = new(new Dictionary<DayOfWeek, HorarioDia>());

    /// <summary>Expediente geral do dia (nulo se não cadastrado).</summary>
    public HorarioDia? Geral(DayOfWeek dia) => geral.GetValueOrDefault(dia);

    /// <summary>Horário personalizado do funcionário no dia (nulo se ele não tem, ou se o dia segue o expediente geral).</summary>
    public HorarioDia? Personalizado(int funcionarioId, DayOfWeek dia) =>
        _personalizados.TryGetValue(funcionarioId, out var dias) ? dias.GetValueOrDefault(dia) : null;

    /// <summary>Horário que vale para o funcionário no dia: o personalizado, senão o geral.</summary>
    public HorarioDia? Do(int funcionarioId, DayOfWeek dia) => Personalizado(funcionarioId, dia) ?? Geral(dia);

    /// <summary>Dia escalado do funcionário, com o horário que vale para ele.</summary>
    public DiaEscalado Dia(int funcionarioId, DateOnly data, Filial? cobertura = null)
    {
        var p = Personalizado(funcionarioId, data.DayOfWeek);
        var h = p ?? Geral(data.DayOfWeek);
        return new DiaEscalado(data, h?.Inicio, h?.Fim, cobertura, p is not null);
    }
}
