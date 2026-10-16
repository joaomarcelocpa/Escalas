using Escalas.Domain.Entidades;

namespace Escalas.Application.Feriados;

public record FilialResumo(int Id, string Nome, string Cidade, string UF);

public record FeriadoLocalDados(int FilialId, DateOnly Data, string Nome, AbrangenciaFeriado Abrangencia);

/// <summary>Cache anual de feriados nacionais e CRUD de feriados estaduais/municipais por filial.</summary>
public interface IFeriadoService
{
    /// <summary>
    /// Garante os feriados nacionais do ano no banco local. Sem <paramref name="forcar"/>, não consulta a API se o ano já está em cache.
    /// Retorna false se precisou da API e ela falhou (o cache existente permanece intacto).
    /// </summary>
    Task<bool> SincronizarAnoAsync(int ano, bool forcar = false, CancellationToken ct = default);

    Task<IReadOnlyList<FilialResumo>> ListarFiliaisAsync(CancellationToken ct = default);

    /// <summary>Feriados estaduais/municipais cadastrados para a filial no ano.</summary>
    Task<IReadOnlyList<Feriado>> ListarLocaisAsync(int filialId, int ano, CancellationToken ct = default);

    /// <summary>Apenas Estadual ou Municipal; UF/Cidade são preenchidos a partir da filial.</summary>
    Task<Feriado> AdicionarLocalAsync(FeriadoLocalDados dados, CancellationToken ct = default);

    Task RemoverLocalAsync(int feriadoId, CancellationToken ct = default);
}
