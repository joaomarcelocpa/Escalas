namespace Escalas.Application.Feriados;

public record FeriadoExterno(DateOnly Data, string Nome);

/// <summary>Cliente da API pública de feriados nacionais (BrasilAPI). Lança exceção em falha de rede/resposta.</summary>
public interface IFeriadosApi
{
    Task<IReadOnlyList<FeriadoExterno>> ObterNacionaisAsync(int ano, CancellationToken ct = default);
}
