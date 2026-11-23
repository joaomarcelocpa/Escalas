using Escalas.Domain.Entidades;

namespace Escalas.Application.Calendario;

/// <summary>Um dia do modelo semanal (segunda a sábado), com os funcionários alocados por setor.</summary>
public record DiaPadraoDto(
    DayOfWeek Dia,
    IReadOnlyList<SetorDiaDto> Setores,
    IReadOnlyList<FuncionarioDisponivelDto> Disponiveis);

public record SemanaPadraoDto(Filial Filial, IReadOnlyList<DiaPadraoDto> Dias);

/// <summary>Modelo semanal que se repete em todas as semanas; o calendário parte dele e aplica exceções por data.</summary>
public interface IEscalaPadraoService
{
    Task<SemanaPadraoDto> ObterAsync(Filial filial, CancellationToken ct = default);

    Task<ResultadoOperacao> AlocarAsync(int funcionarioId, DayOfWeek dia, CancellationToken ct = default);

    Task<ResultadoOperacao> DesalocarAsync(int funcionarioId, DayOfWeek dia, CancellationToken ct = default);

    Task<ResultadoOperacao> MoverAsync(int funcionarioId, DayOfWeek origem, DayOfWeek destino, CancellationToken ct = default);

    /// <summary>Aloca o funcionário de segunda a sábado.</summary>
    Task<ResultadoOperacao> AlocarTodosOsDiasAsync(int funcionarioId, CancellationToken ct = default);
}
