using Escalas.Application.Calendario;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Calendario;

public class ExpedienteService(IDbContextFactory<EscalasDbContext> factory) : IExpedienteService
{
    private static readonly string[] Nomes = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

    public async Task<IReadOnlyList<ExpedienteDto>> ObterAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Expedientes.AsNoTracking().ToListAsync(ct))
            .OrderBy(e => ((int)e.DiaSemana + 6) % 7) // segunda primeiro
            .Select(e => new ExpedienteDto(e.DiaSemana, e.Inicio, e.Fim))
            .ToList();
    }

    public async Task<ResultadoOperacao> SalvarAsync(IReadOnlyList<ExpedienteEntrada> dias, CancellationToken ct = default)
    {
        foreach (var d in dias)
        {
            if (d.Inicio is null != d.Fim is null)
                return ResultadoOperacao.Falha($"{Nomes[(int)d.Dia]}: preencha início e fim, ou deixe os dois vazios.");
            if (d.Inicio is { } i && d.Fim is { } f && RegraHoras.Validar(i, f) is { } erro)
                return ResultadoOperacao.Falha($"{Nomes[(int)d.Dia]}: {erro}");
        }
        if (dias.Select(d => d.Dia).Distinct().Count() != dias.Count)
            return ResultadoOperacao.Falha("Dia da semana repetido.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var existentes = await db.Expedientes.ToDictionaryAsync(e => e.DiaSemana, ct);

        foreach (var d in dias)
        {
            existentes.TryGetValue(d.Dia, out var atual);
            if (d.Inicio is not { } inicio || d.Fim is not { } fim)
            {
                if (atual is not null) db.Expedientes.Remove(atual);
            }
            else if (atual is null)
                db.Expedientes.Add(new Expediente { DiaSemana = d.Dia, Inicio = inicio, Fim = fim });
            else
            {
                atual.Inicio = inicio;
                atual.Fim = fim;
            }
        }
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }
}
