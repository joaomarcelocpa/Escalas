namespace Escalas.Application.Calendario;

public enum StatusDia { Normal, Feriado, Fechado }

public record FilialResumo(int Id, string Nome);

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
public record SemanaDto(int FilialId, DateOnly Inicio, IReadOnlyList<DiaDto> Dias);

public record ResultadoOperacao(bool Ok, string? Erro = null)
{
    public static readonly ResultadoOperacao Sucesso = new(true);
    public static ResultadoOperacao Falha(string erro) => new(false, erro);
}

public interface IEscalaService
{
    Task<IReadOnlyList<FilialResumo>> ListarFiliaisAsync(CancellationToken ct = default);

    /// <param name="qualquerDiaDaSemana">Qualquer data; a semana retornada começa na segunda-feira correspondente.</param>
    Task<SemanaDto> ObterSemanaAsync(int filialId, DateOnly qualquerDiaDaSemana, CancellationToken ct = default);

    Task<ResultadoOperacao> EscalarAsync(int funcionarioId, DateOnly data, CancellationToken ct = default);

    Task<ResultadoOperacao> RemoverAsync(int funcionarioId, DateOnly data, CancellationToken ct = default);

    Task<ResultadoOperacao> MoverAsync(int funcionarioId, DateOnly origem, DateOnly destino, CancellationToken ct = default);

    Task<ResultadoOperacao> DefinirDiaFechadoAsync(int filialId, DateOnly data, bool fechado, CancellationToken ct = default);
}
