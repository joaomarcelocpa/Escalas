using Escalas.Application.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Calendario;
using Microsoft.Data.Sqlite;

namespace Escalas.Tests.Calendario;

public sealed class HorasServiceTests : IDisposable
{
    private static readonly DateOnly Segunda = new(2026, 10, 12);

    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly EscalaServiceTests.FabricaTeste _fabrica;
    private readonly EscalaServiceTests.FeriadosFake _feriados = new();
    private readonly RelogioFixo _relogio = new(Segunda.AddDays(2)); // quarta
    private readonly ExpedienteService _expedientes;
    private readonly HorasService _servico;

    public HorasServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _conexao.Open();
        _fabrica = new EscalaServiceTests.FabricaTeste(_conexao);
        using (var db = _fabrica.CreateDbContext()) db.Database.EnsureCreated();
        var escalas = new EscalaService(_fabrica, _feriados);
        _expedientes = new ExpedienteService(_fabrica);
        _servico = new HorasService(_fabrica, escalas, _expedientes, _relogio);
        Semear();
    }

    public void Dispose() => _conexao.Dispose();

    // Matriz: Ana(1, Caixa) seg–sex; Bia(2, Caixa) sem modelo; Dora(4) inativa. FC: Eva(5, Salão) seg e sáb.
    private void Semear()
    {
        using var db = _fabrica.CreateDbContext();
        db.Setores.AddRange(
            new Setor { Id = 1, Filial = Filial.Matriz, Nome = "Caixa" },
            new Setor { Id = 3, Filial = Filial.FC, Nome = "Salão" });
        db.Funcionarios.AddRange(
            new Funcionario { Id = 1, Filial = Filial.Matriz, SetorId = 1, Nome = "Ana" },
            new Funcionario { Id = 2, Filial = Filial.Matriz, SetorId = 1, Nome = "Bia" },
            new Funcionario { Id = 4, Filial = Filial.Matriz, SetorId = 1, Nome = "Dora", Ativo = false },
            new Funcionario { Id = 5, Filial = Filial.FC, SetorId = 3, Nome = "Eva" });
        foreach (var dia in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = 1, DiaSemana = dia });
        db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = 5, DiaSemana = DayOfWeek.Monday });
        db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = 5, DiaSemana = DayOfWeek.Saturday });
        db.SaveChanges();
    }

    /// <summary>Expediente do dia da semana de Segunda + deslocamento (0 = segunda).</summary>
    private Task Expediente(int deslocamento, int inicio, int fim) =>
        _expedientes.SalvarAsync([new ExpedienteEntrada(Segunda.AddDays(deslocamento).DayOfWeek, new TimeOnly(inicio, 0), new TimeOnly(fim, 0))]);

    [Fact]
    public async Task Funcionario_SomaExpedienteDosDiasEscalados_eAteHoje()
    {
        for (var i = 0; i < 5; i++) await Expediente(i, 8, 17); // 9h de expediente − 1h30 de almoço = 7h30 por dia

        var h = await _servico.ObterFuncionarioAsync(1, Segunda.AddDays(3));

        Assert.NotNull(h);
        Assert.Equal(Segunda, h.InicioSemana);
        Assert.Equal(5, h.Dias.Count);
        Assert.Equal(TimeSpan.FromHours(37.5), h.Totais.Total);
        Assert.Equal(TimeSpan.FromHours(22.5), h.Totais.AteHoje); // seg, ter, qua
        Assert.All(h.Dias, d => Assert.True(d.DescontaAlmoco));
        Assert.Equal(0, h.Totais.DiasSemExpediente);
        Assert.Equal("Caixa", h.Setor);
    }

    [Fact]
    public async Task DiaSemExpediente_AparecemSemHoras_eNaoSoma()
    {
        await Expediente(0, 8, 12);

        var h = (await _servico.ObterFuncionarioAsync(1, Segunda))!;

        Assert.Equal(TimeSpan.FromHours(4), h.Totais.Total);
        Assert.Equal(4, h.Totais.DiasSemExpediente);
        Assert.Null(h.Dias[1].Horas);
    }

    [Fact]
    public async Task DiaFechado_NaoContaComoTrabalhado()
    {
        for (var i = 0; i < 5; i++) await Expediente(i, 8, 17);
        await new EscalaService(_fabrica, _feriados).DefinirDiaFechadoAsync(Filial.Matriz, Segunda.AddDays(1), true);

        var h = (await _servico.ObterFuncionarioAsync(1, Segunda))!;

        Assert.Equal(4, h.Dias.Count);
        Assert.DoesNotContain(h.Dias, d => d.Data == Segunda.AddDays(1));
        Assert.Equal(TimeSpan.FromHours(30), h.Totais.Total); // 4 × 7h30
    }

    [Fact]
    public async Task ExpedienteValeParaTodasAsFiliais()
    {
        await Expediente(0, 7, 15); // 8h → 6h30 após o almoço
        await Expediente(5, 8, 12); // 4h, sem almoço

        var eva = (await _servico.ObterFuncionarioAsync(5, Segunda))!;

        Assert.Equal(Filial.FC, eva.Filial);
        Assert.Equal(TimeSpan.FromHours(10.5), eva.Totais.Total);
    }

    [Fact]
    public async Task Foto_ChegaNoPopupENoResumo()
    {
        await using (var db = _fabrica.CreateDbContext())
        {
            db.Funcionarios.Find(1)!.Foto = [1, 2, 3];
            db.SaveChanges();
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, (await _servico.ObterFuncionarioAsync(1, Segunda))!.Foto);
        var matriz = (await _servico.ObterResumoAsync(Segunda)).Filiais.Single(f => f.Filial == Filial.Matriz);
        Assert.Equal(new byte[] { 1, 2, 3 }, matriz.Funcionarios.Single(f => f.Nome == "Ana").Foto);
        Assert.Null(matriz.Funcionarios.Single(f => f.Nome == "Bia").Foto);
    }

    [Fact]
    public async Task FuncionarioInexistente_RetornaNull()
    {
        Assert.Null(await _servico.ObterFuncionarioAsync(99, Segunda));
    }

    [Fact]
    public async Task Resumo_AgrupaPorFilial_ComTodosOsAtivos()
    {
        for (var i = 0; i < 6; i++) await Expediente(i, 8, 16); // 8h − 1h30 = 6h30, seg a sáb

        var r = await _servico.ObterResumoAsync(Segunda.AddDays(4));

        Assert.Equal(Segunda, r.Inicio);
        Assert.Equal(Filiais.Todas.Select(f => f.Id), r.Filiais.Select(f => f.Filial));
        var matriz = r.Filiais.Single(f => f.Filial == Filial.Matriz);
        Assert.Equal(["Ana", "Bia"], matriz.Funcionarios.Select(f => f.Nome).ToArray()); // Dora inativa fica fora
        Assert.Equal(TimeSpan.FromHours(32.5), matriz.Funcionarios[0].Totais.Total);
        Assert.Empty(matriz.Funcionarios[1].Dias);
        Assert.Equal(TimeSpan.FromHours(32.5), matriz.Total);
        Assert.Equal(TimeSpan.FromHours(13), r.Filiais.Single(f => f.Filial == Filial.FC).Total);
        Assert.Empty(r.DiasSemExpediente); // domingo sem expediente, mas fechado: ninguém escalado
    }

    [Fact]
    public async Task Resumo_ApontaDatasComEscaladosSemExpediente()
    {
        await Expediente(0, 8, 16); // só segunda

        var r = await _servico.ObterResumoAsync(Segunda);

        // Ana trabalha seg–sex; Eva seg e sáb: faltam ter, qua, qui, sex e sáb.
        Assert.Equal(Enumerable.Range(1, 5).Select(i => Segunda.AddDays(i)), r.DiasSemExpediente);
    }

    [Fact]
    public async Task ExpedienteDoDiaDaSemana_ValeEmTodasAsSemanas()
    {
        await Expediente(0, 8, 17);

        var estaSemana = (await _servico.ObterFuncionarioAsync(5, Segunda))!;
        var proxima = (await _servico.ObterFuncionarioAsync(5, Segunda.AddDays(7)))!;

        Assert.Equal(TimeSpan.FromHours(7.5), estaSemana.Totais.Total);
        Assert.Equal(TimeSpan.FromHours(7.5), proxima.Totais.Total);
    }

    [Fact]
    public async Task Salvar_ExpedienteInvalido_NaoGravaNada()
    {
        var r = await _expedientes.SalvarAsync([
            new ExpedienteEntrada(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
            new ExpedienteEntrada(DayOfWeek.Tuesday, new TimeOnly(18, 0), new TimeOnly(8, 0)),
        ]);

        Assert.False(r.Ok);
        Assert.Empty(await _expedientes.ObterAsync());
    }

    [Fact]
    public async Task Salvar_SoInicioOuSoFim_EhInvalido()
    {
        var r = await _expedientes.SalvarAsync([new ExpedienteEntrada(DayOfWeek.Monday, new TimeOnly(8, 0), null)]);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Salvar_AtualizaERemove()
    {
        await Expediente(0, 8, 17);
        await Expediente(1, 8, 17);

        await _expedientes.SalvarAsync([
            new ExpedienteEntrada(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
            new ExpedienteEntrada(DayOfWeek.Tuesday, null, null),
        ]);

        var lista = await _expedientes.ObterAsync();
        Assert.Equal([new ExpedienteDto(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(18, 0))], lista);
    }

    private sealed class RelogioFixo(DateOnly hoje) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(hoje.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
