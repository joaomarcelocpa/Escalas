using Escalas.Domain.Entidades;

namespace Escalas.Application.Feriados;

/// <summary>Contrato de consulta de feriados usado pelo calendário. Implementado em Infrastructure (módulo Feriados).</summary>
public interface IFeriadoConsulta
{
    /// <summary>Feriados aplicáveis à filial (nacionais e os estaduais/municipais cadastrados para ela) no intervalo, inclusive.</summary>
    Task<IReadOnlyList<Feriado>> ObterAsync(Filial filial, DateOnly inicio, DateOnly fim, CancellationToken ct = default);
}
