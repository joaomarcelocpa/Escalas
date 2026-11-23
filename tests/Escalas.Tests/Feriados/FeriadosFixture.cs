using Escalas.Application.Feriados;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Feriados;
using Escalas.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Tests.Feriados;

/// <summary>Banco SQLite em memória (conexão mantida aberta) + factory de contexto.</summary>
public sealed class BancoMemoria : IDbContextFactory<EscalasDbContext>, IDisposable
{
    private readonly SqliteConnection _conexao;

    static BancoMemoria() => SQLitePCL.Batteries_V2.Init();

    public BancoMemoria()
    {
        _conexao = new SqliteConnection("Data Source=:memory:");
        _conexao.Open();
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public EscalasDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<EscalasDbContext>().UseSqlite(_conexao).Options);

    public void Dispose() => _conexao.Dispose();
}

public sealed class ApiFalsa : IFeriadosApi
{
    public Dictionary<int, List<FeriadoExterno>> PorAno { get; } = new();
    public bool Offline { get; set; }
    public List<int> Chamadas { get; } = [];

    public Task<IReadOnlyList<FeriadoExterno>> ObterNacionaisAsync(int ano, CancellationToken ct = default)
    {
        Chamadas.Add(ano);
        if (Offline) throw new HttpRequestException("sem rede");
        return Task.FromResult<IReadOnlyList<FeriadoExterno>>(PorAno.GetValueOrDefault(ano) ?? []);
    }
}

public sealed class RelogioFalso : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Agora;
}

public sealed class HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<Uri> Requisicoes { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requisicoes.Add(request.RequestUri!);
        return Task.FromResult(responder(request));
    }
}

public sealed class HttpFactoryFalsa(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("https://brasilapi.com.br/") };
}
