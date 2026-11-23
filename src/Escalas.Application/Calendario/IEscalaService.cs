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
