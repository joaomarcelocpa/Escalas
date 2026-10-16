using Escalas.Application.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Calendario;
using Escalas.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;

namespace Escalas.Tests.Calendario;

public sealed class EscalaPadraoTests : IDisposable
{
    private static readonly DateOnly Segunda = new(2026, 10, 12);
    private static readonly DateOnly SegundaSeguinte = Segunda.AddDays(7);

    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly EscalaServiceTests.FabricaTeste _fabrica;
    private readonly EscalaPadraoService _padrao;
    private readonly EscalaService _calendario;

    public EscalaPadraoTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _conexao.Open();
        _fabrica = new EscalaServiceTests.FabricaTeste(_conexao);
        using (var db = _fabrica.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Filiais.Add(new Filial { Id = 1, Nome = "Centro", Cidade = "Recife", UF = "PE" });
            db.Setores.Add(new Setor { Id = 1, FilialId = 1, Nome = "Caixa" });
            db.Funcionarios.AddRange(
                new Funcionario { Id = 1, FilialId = 1, SetorId = 1, Nome = "Ana" },
                new Funcionario { Id = 2, FilialId = 1, SetorId = 1, Nome = "Bia" });
            db.SaveChanges();
        }
        _padrao = new EscalaPadraoService(_fabrica);
        _calendario = new EscalaService(_fabrica, new EscalaServiceTests.FeriadosFake());
    }

    public void Dispose() => _conexao.Dispose();

    private async Task<List<int>> EscaladosAsync(DateOnly data)
    {
        var dia = (await _calendario.ObterSemanaAsync(1, data)).Dias.Single(d => d.Data == data);
        return dia.Setores.SelectMany(s => s.Escalados).Select(f => f.Id).OrderBy(i => i).ToList();
    }

    [Fact]
    public async Task Modelo_AplicaSeEmTodasAsSemanas()
    {
        await _padrao.AlocarAsync(1, DayOfWeek.Monday);

        Assert.Equal([1], await EscaladosAsync(Segunda));
        Assert.Equal([1], await EscaladosAsync(SegundaSeguinte));
        Assert.Empty(await EscaladosAsync(Segunda.AddDays(1)));
    }

    [Fact]
    public async Task Remocao_NoCalendario_AfetaSoAquelaData()
    {
        await _padrao.AlocarAsync(1, DayOfWeek.Monday);

        Assert.True((await _calendario.RemoverAsync(1, Segunda)).Ok);

        Assert.Empty(await EscaladosAsync(Segunda));
        Assert.Equal([1], await EscaladosAsync(SegundaSeguinte));
    }

    [Fact]
    public async Task Inclusao_NoCalendario_AfetaSoAquelaData()
    {
        Assert.True((await _calendario.EscalarAsync(2, Segunda)).Ok);

        Assert.Equal([2], await EscaladosAsync(Segunda));
        Assert.Empty(await EscaladosAsync(SegundaSeguinte));
    }

    [Fact]
    public async Task EscalarDeNovoUmDiaRemovido_VoltaAoModelo()
    {
        await _padrao.AlocarAsync(1, DayOfWeek.Monday);
        await _calendario.RemoverAsync(1, Segunda);

        Assert.True((await _calendario.EscalarAsync(1, Segunda)).Ok);

        Assert.Equal([1], await EscaladosAsync(Segunda));
        Assert.False((await _calendario.EscalarAsync(1, Segunda)).Ok); // já escalado
    }

    [Fact]
    public async Task Mover_NoCalendario_RetiraDaOrigemEIncluiNoDestino()
    {
        await _padrao.AlocarAsync(1, DayOfWeek.Monday);
        var terca = Segunda.AddDays(1);

        Assert.True((await _calendario.MoverAsync(1, Segunda, terca)).Ok);

        Assert.Empty(await EscaladosAsync(Segunda));
        Assert.Equal([1], await EscaladosAsync(terca));
        Assert.Equal([1], await EscaladosAsync(SegundaSeguinte)); // semana seguinte segue o modelo
        Assert.Empty(await EscaladosAsync(SegundaSeguinte.AddDays(1)));
    }

    [Fact]
    public async Task DiaFechado_NaoMostraEscaladosDoModelo()
    {
        await _padrao.AlocarAsync(1, DayOfWeek.Monday);
        await _calendario.DefinirDiaFechadoAsync(1, Segunda, true);

        Assert.Empty(await EscaladosAsync(Segunda));

        await _calendario.DefinirDiaFechadoAsync(1, Segunda, false);
        Assert.Equal([1], await EscaladosAsync(Segunda));
    }

    [Fact]
    public async Task Modelo_NaoAceitaDomingoNemFuncionarioInativo()
    {
        Assert.False((await _padrao.AlocarAsync(1, DayOfWeek.Sunday)).Ok);

        await using (var db = _fabrica.CreateDbContext())
        {
            db.Funcionarios.Find(2)!.Ativo = false;
            db.SaveChanges();
        }
        Assert.False((await _padrao.AlocarAsync(2, DayOfWeek.Monday)).Ok);
    }

    [Fact]
    public async Task Modelo_MoverEDesalocarEAlocarTodosOsDias()
    {
        await _padrao.AlocarAsync(1, DayOfWeek.Monday);
        Assert.True((await _padrao.MoverAsync(1, DayOfWeek.Monday, DayOfWeek.Wednesday)).Ok);
        Assert.False((await _padrao.AlocarAsync(1, DayOfWeek.Wednesday)).Ok);

        var semana = await _padrao.ObterAsync(1);
        Assert.Equal(6, semana.Dias.Count);
        Assert.DoesNotContain(semana.Dias.Single(d => d.Dia == DayOfWeek.Wednesday).Disponiveis, f => f.Id == 1);

        Assert.True((await _padrao.DesalocarAsync(1, DayOfWeek.Wednesday)).Ok);
        Assert.True((await _padrao.AlocarTodosOsDiasAsync(2)).Ok);
        Assert.All((await _padrao.ObterAsync(1)).Dias,
            d => Assert.Contains(d.Setores.SelectMany(s => s.Escalados), f => f.Id == 2));
    }
}
