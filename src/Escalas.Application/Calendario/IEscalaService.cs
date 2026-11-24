using Escalas.Domain.Entidades;

namespace Escalas.Application.Calendario;

public enum StatusDia { Normal, Feriado, Fechado }

public record FuncionarioEscaladoDto(int Id, string Nome, byte[]? Foto = null);

public record SetorDiaDto(
    int SetorId,
    string Nome,
    int Minimo,
    IReadOnlyList<FuncionarioEscaladoDto> Escalados,
    bool AbaixoDoMinimo);

/// <summary>
/// Funcionário que trabalha, neste dia, em uma filial diferente da dele para cobrir folga (filial extra).
/// </summary>
/// <param name="Setor">Setor na filial de origem.</param>
/// <param name="Filial">Em <c>Cobrindo</c> e <c>Candidatos</c>: a filial de origem. Em <c>Fora</c>: a filial coberta.</param>
public record CoberturaDiaDto(int Id, string Nome, string Setor, Filial Filial, byte[]? Foto = null);

public record FuncionarioDisponivelDto(int Id, string Nome, int SetorId, string SetorNome, byte[]? Foto = null);

/// <param name="NomeFeriado">Preenchido sempre que houver feriado, mesmo com o dia fechado.</param>
public record DiaDto(
    DateOnly Data,
    StatusDia Status,
    string? NomeFeriado,
    IReadOnlyList<SetorDiaDto> Setores,
    IReadOnlyList<FuncionarioDisponivelDto> Disponiveis)
{
    public bool Fechado => Status == StatusDia.Fechado;

    /// <summary>Funcionários de outras filiais cobrindo folga nesta filial neste dia (não entram em <see cref="Setores"/>).</summary>
    public IReadOnlyList<CoberturaDiaDto> Cobrindo { get; init; } = [];

    /// <summary>Funcionários desta filial que estão cobrindo outra filial neste dia: não aparecem como escalados nem como folga.</summary>
    public IReadOnlyList<CoberturaDiaDto> Fora { get; init; } = [];
}

/// <summary>Semana segunda a domingo.</summary>
public record SemanaDto(Filial Filial, DateOnly Inicio, IReadOnlyList<DiaDto> Dias);

public record ResultadoOperacao(bool Ok, string? Erro = null)
{
    public static readonly ResultadoOperacao Sucesso = new(true);
    public static ResultadoOperacao Falha(string erro) => new(false, erro);
}

public interface IEscalaService
{
    /// <param name="qualquerDiaDaSemana">Qualquer data; a semana retornada começa na segunda-feira correspondente.</param>
    Task<SemanaDto> ObterSemanaAsync(Filial filial, DateOnly qualquerDiaDaSemana, CancellationToken ct = default);

    Task<ResultadoOperacao> DefinirDiaFechadoAsync(Filial filial, DateOnly data, bool fechado, CancellationToken ct = default);
}
