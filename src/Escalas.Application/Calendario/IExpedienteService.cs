using Escalas.Domain.Calendario;

namespace Escalas.Application.Calendario;

public record ExpedienteDto(DayOfWeek Dia, TimeOnly Inicio, TimeOnly Fim);

/// <summary>Um dia da semana a gravar. Início e fim nulos removem o expediente do dia.</summary>
public record ExpedienteEntrada(DayOfWeek Dia, TimeOnly? Inicio, TimeOnly? Fim);

/// <summary>Expediente geral por dia da semana (início e fim), igual para todas as filiais e todas as semanas.</summary>
public interface IExpedienteService
{
    /// <summary>Expedientes cadastrados, de segunda a domingo.</summary>
    Task<IReadOnlyList<ExpedienteDto>> ObterAsync(CancellationToken ct = default);

    /// <summary>Grava vários dias de uma vez; se algum for inválido, nada é gravado.</summary>
    /// <summary>
    /// Expediente geral junto com os horários personalizados dos funcionários que têm a opção ativa:
    /// é o que vale para calcular as horas de cada um.
    /// </summary>
    Task<TabelaHorarios> ObterTabelaAsync(CancellationToken ct = default);

    Task<ResultadoOperacao> SalvarAsync(IReadOnlyList<ExpedienteEntrada> dias, CancellationToken ct = default);
}
