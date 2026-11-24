using Escalas.Domain.Entidades;

namespace Escalas.Application.Calendario;

/// <summary>Um dia do modelo semanal (segunda a sábado), com os funcionários alocados por setor.</summary>
public record DiaPadraoDto(
    DayOfWeek Dia,
    IReadOnlyList<SetorDiaDto> Setores,
    IReadOnlyList<FuncionarioDisponivelDto> Disponiveis)
{
    /// <summary>Funcionários de outras filiais que cobrem esta filial neste dia da semana.</summary>
    public IReadOnlyList<CoberturaDiaDto> Cobrindo { get; init; } = [];

    /// <summary>Funcionários desta filial que, neste dia da semana, estão cobrindo outra filial.</summary>
    public IReadOnlyList<CoberturaDiaDto> Fora { get; init; } = [];

    /// <summary>
    /// Quem pode cobrir esta filial neste dia: ativos com esta filial como extra, que trabalham na filial deles neste dia.
    /// Só é preenchido quando algum funcionário fixo desta filial está de folga no dia.
    /// </summary>
    public IReadOnlyList<CoberturaDiaDto> Candidatos { get; init; } = [];
}

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

    /// <summary>
    /// O funcionário passa a cobrir <paramref name="filial"/> (a filial extra dele) neste dia da semana, em todas as semanas.
    /// Ele precisa trabalhar na própria filial nesse dia: sai de lá e vai para a filial coberta (nunca fica nas duas).
    /// </summary>
    Task<ResultadoOperacao> CobrirAsync(int funcionarioId, Filial filial, DayOfWeek dia, CancellationToken ct = default);

    /// <summary>Desfaz a cobertura do dia: o funcionário volta a trabalhar na própria filial nesse dia.</summary>
    Task<ResultadoOperacao> RemoverCoberturaAsync(int funcionarioId, DayOfWeek dia, CancellationToken ct = default);
}
