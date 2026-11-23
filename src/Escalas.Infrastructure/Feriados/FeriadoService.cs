using System.Collections.Concurrent;
using Escalas.Application.Feriados;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Feriados;

/// <summary>Implementa o cache de feriados, o CRUD local e a consulta usada pelo calendário. Deve ser singleton (guarda o backoff de falhas).</summary>
public sealed class FeriadoService(
    IDbContextFactory<EscalasDbContext> dbFactory,
    IFeriadosApi api,
    TimeProvider relogio) : IFeriadoService, IFeriadoConsulta
{
    private static readonly TimeSpan EsperaAposFalha = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<int, DateTimeOffset> _falhas = new();

    public async Task<bool> SincronizarAnoAsync(int ano, bool forcar = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ini = new DateOnly(ano, 1, 1);
        var fim = new DateOnly(ano, 12, 31);

        if (!forcar && await db.Feriados.AnyAsync(f => f.Abrangencia == AbrangenciaFeriado.Nacional && f.Data >= ini && f.Data <= fim, ct))
            return true;

        IReadOnlyList<FeriadoExterno> baixados;
        try
        {
            baixados = await api.ObterNacionaisAsync(ano, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException
                                      or System.Text.Json.JsonException or FormatException)
        {
            if (ct.IsCancellationRequested) throw;
            _falhas[ano] = relogio.GetUtcNow();
            return false;
        }

        if (baixados.Count == 0) return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var antigos = await db.Feriados
            .Where(f => f.Abrangencia == AbrangenciaFeriado.Nacional && f.Data >= ini && f.Data <= fim)
            .ToListAsync(ct);
        db.Feriados.RemoveRange(antigos);
        db.Feriados.AddRange(baixados
            .Where(b => b.Data.Year == ano)
            .DistinctBy(b => (b.Data, b.Nome))
            .Select(b => new Feriado { Data = b.Data, Nome = b.Nome, Abrangencia = AbrangenciaFeriado.Nacional }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _falhas.TryRemove(ano, out _);
        return true;
    }

    public async Task<IReadOnlyList<Feriado>> ListarLocaisAsync(Filial filial, int ano, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ini = new DateOnly(ano, 1, 1);
        var fim = new DateOnly(ano, 12, 31);
        return await db.Feriados.AsNoTracking()
            .Where(f => f.Filial == filial && f.Abrangencia != AbrangenciaFeriado.Nacional && f.Data >= ini && f.Data <= fim)
            .OrderBy(f => f.Data).ToListAsync(ct);
    }

    public async Task<Feriado> AdicionarLocalAsync(FeriadoLocalDados dados, CancellationToken ct = default)
    {
        if (dados.Abrangencia == AbrangenciaFeriado.Nacional)
            throw new ArgumentException("Feriados nacionais vêm da API; cadastre apenas estaduais ou municipais.");
        var nome = dados.Nome?.Trim();
        if (string.IsNullOrEmpty(nome)) throw new ArgumentException("Informe o nome do feriado.");

        if (!Filiais.Existe(dados.Filial)) throw new ArgumentException("Filial não encontrada.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var feriado = new Feriado
        {
            Data = dados.Data,
            Nome = nome,
            Abrangencia = dados.Abrangencia,
            Filial = dados.Filial,
        };
        db.Feriados.Add(feriado);
        await db.SaveChangesAsync(ct);
        return feriado;
    }

    public async Task RemoverLocalAsync(int feriadoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var f = await db.Feriados.FindAsync([feriadoId], ct);
        if (f is null) return;
        if (f.Abrangencia == AbrangenciaFeriado.Nacional)
            throw new InvalidOperationException("Feriados nacionais não podem ser removidos.");
        db.Feriados.Remove(f);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Feriado>> ObterAsync(Filial filial, DateOnly inicio, DateOnly fim, CancellationToken ct = default)
    {
        for (var ano = inicio.Year; ano <= fim.Year; ano++)
        {
            if (_falhas.TryGetValue(ano, out var quando) && relogio.GetUtcNow() - quando < EsperaAposFalha) continue;
            await SincronizarAnoAsync(ano, ct: ct);
        }

        // As filiais são fixas e não têm cidade/UF: feriados estaduais e municipais valem pela filial a que foram vinculados.
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Feriados.AsNoTracking()
            .Where(f => f.Data >= inicio && f.Data <= fim
                        && (f.Abrangencia == AbrangenciaFeriado.Nacional || f.Filial == filial))
            .OrderBy(f => f.Data).ThenBy(f => f.Abrangencia)
            .ToListAsync(ct);
    }
}
