using Escalas.Domain.Entidades;

namespace Escalas.Application.Feriados;

/// <summary>Contrato de consulta de feriados usado pelo calendário. Implementado em Infrastructure (módulo Feriados).</summary>
public interface IFeriadoConsulta
{
    /// <summary>Feriados aplicáveis à filial (nacionais, estaduais da UF e locais) no intervalo, inclusive.</summary>
    Task<IReadOnlyList<Feriado>> ObterAsync(int filialId, DateOnly inicio, DateOnly fim, CancellationToken ct = default);
}
