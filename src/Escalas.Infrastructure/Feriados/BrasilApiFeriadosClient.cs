using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Escalas.Application.Feriados;

namespace Escalas.Infrastructure.Feriados;

public sealed class BrasilApiFeriadosClient(IHttpClientFactory httpFactory) : IFeriadosApi
{
    public const string NomeClient = "brasilapi";

    private sealed record Item([property: JsonPropertyName("date")] string Date, [property: JsonPropertyName("name")] string Name);

    public async Task<IReadOnlyList<FeriadoExterno>> ObterNacionaisAsync(int ano, CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient(NomeClient);
        var itens = await http.GetFromJsonAsync<List<Item>>($"api/feriados/v1/{ano}", ct)
                    ?? throw new InvalidOperationException("Resposta vazia da BrasilAPI.");
        return itens.Select(i => new FeriadoExterno(DateOnly.ParseExact(i.Date, "yyyy-MM-dd"), i.Name)).ToList();
    }
}
