using ClosedXML.Excel;
using Escalas.Application.Cadastros;
using Escalas.Application.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Cadastros;
using Escalas.Infrastructure.Calendario;
using Escalas.Infrastructure.Exportacao;
using Microsoft.Data.Sqlite;

namespace Escalas.Tests.Calendario;

/// <summary>
/// Cobertura de folga: funcionário com filial extra cobre, no modelo semanal, o dia de folga dos fixos da filial extra.
/// </summary>
public sealed class CoberturaTests : IDisposable
{
    private static readonly DateOnly Segunda = new(2026, 10, 12);
    private static readonly DateOnly Quarta = Segunda.AddDays(2);

    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly EscalaServiceTests.FabricaTeste _fabrica;
    private readonly EscalaPadraoService _padrao;
    private readonly EscalaService _calendario;
    private readonly ExpedienteService _expedientes;
    private readonly HorasService _horas;

    // Matriz: Ana (1, Caixa, extra Interiores) e Bia (2, Caixa, sem extra) de segunda a sábado.
    // Interiores: Ivo (3, Caixa, mínimo 1) de segunda a sábado, com folga na quarta.
    // Caio (4, Matriz, extra Interiores) inativo.
    public CoberturaTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _conexao.Open();
        _fabrica = new EscalaServiceTests.FabricaTeste(_conexao);
        using (var db = _fabrica.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Setores.AddRange(
                new Setor { Id = 1, Filial = Filial.Matriz, Nome = "Caixa" },
                new Setor { Id = 2, Filial = Filial.Interiores, Nome = "Caixa", MinimoFuncionarios = 1 });
            db.Funcionarios.AddRange(
                new Funcionario { Id = 1, Filial = Filial.Matriz, SetorId = 1, Nome = "Ana", FilialExtra = Filial.Interiores },
                new Funcionario { Id = 2, Filial = Filial.Matriz, SetorId = 1, Nome = "Bia" },
                new Funcionario { Id = 3, Filial = Filial.Interiores, SetorId = 2, Nome = "Ivo" },
                new Funcionario { Id = 4, Filial = Filial.Matriz, SetorId = 1, Nome = "Caio", FilialExtra = Filial.Interiores, Ativo = false });
            foreach (var dia in EscalaPadraoService.DiasUteis)
            {
                db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = 1, DiaSemana = dia });
                db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = 2, DiaSemana = dia });
                db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = 4, DiaSemana = dia });
                if (dia != DayOfWeek.Wednesday) db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = 3, DiaSemana = dia });
            }
            db.SaveChanges();
        }
        _padrao = new EscalaPadraoService(_fabrica);
        _calendario = new EscalaService(_fabrica, new EscalaServiceTests.FeriadosFake());
        _expedientes = new ExpedienteService(_fabrica);
        _horas = new HorasService(_fabrica, _calendario, _expedientes, new RelogioFixo(Quarta));
    }

    public void Dispose() => _conexao.Dispose();

    private async Task<DiaPadraoDto> DiaPadrao(Filial filial, DayOfWeek dia) =>
        (await _padrao.ObterAsync(filial)).Dias.Single(d => d.Dia == dia);

    private async Task<DiaDto> DiaCalendario(Filial filial, DateOnly data) =>
        (await _calendario.ObterSemanaAsync(filial, data)).Dias.Single(d => d.Data == data);

    private static IEnumerable<int> Escalados(DiaDto dia) => dia.Setores.SelectMany(s => s.Escalados).Select(f => f.Id);

    private Task ExpedienteSegASab() => _expedientes.SalvarAsync(EscalaPadraoService.DiasUteis
        .Select(d => new ExpedienteEntrada(d, new TimeOnly(8, 0), new TimeOnly(17, 0))).ToList()); // 7h30 por dia

    // ───────────── Escala semanal ─────────────

    [Fact]
    public async Task Candidatos_SoNoDiaDeFolgaDoFixo_eSoQuemTemAFilialComoExtra()
    {
        var quarta = await DiaPadrao(Filial.Interiores, DayOfWeek.Wednesday);
        Assert.Equal([1], quarta.Candidatos.Select(c => c.Id));         // Bia não tem extra; Caio está inativo
        Assert.Equal(Filial.Matriz, quarta.Candidatos[0].Filial);
        Assert.Empty((await DiaPadrao(Filial.Interiores, DayOfWeek.Monday)).Candidatos); // Ivo trabalha: sem folga para cobrir
    }

    [Fact]
    public async Task Cobrir_TiraDaPropriaFilialNoDia_eColocaNaFilialCoberta()
    {
        var r = await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);
        Assert.True(r.Ok, r.Erro);

        var interiores = await DiaPadrao(Filial.Interiores, DayOfWeek.Wednesday);
        Assert.Equal([1], interiores.Cobrindo.Select(c => c.Id));
        Assert.Empty(interiores.Candidatos);

        var matriz = await DiaPadrao(Filial.Matriz, DayOfWeek.Wednesday);
        Assert.DoesNotContain(1, matriz.Setores.SelectMany(s => s.Escalados).Select(f => f.Id));
        Assert.DoesNotContain(matriz.Disponiveis, f => f.Id == 1); // não aparece como folga
        var fora = Assert.Single(matriz.Fora);
        Assert.Equal((1, Filial.Interiores), (fora.Id, fora.Filial));

        // Os outros dias continuam iguais.
        Assert.Contains(1, (await DiaPadrao(Filial.Matriz, DayOfWeek.Tuesday)).Setores.SelectMany(s => s.Escalados).Select(f => f.Id));
    }

    [Fact]
    public async Task Cobrir_RecusaQuemNaoTemAFilialComoExtra_eAPropriaFilial()
    {
        Assert.False((await _padrao.CobrirAsync(2, Filial.Interiores, DayOfWeek.Wednesday)).Ok);
        Assert.False((await _padrao.CobrirAsync(1, Filial.Matriz, DayOfWeek.Wednesday)).Ok);
        Assert.False((await _padrao.CobrirAsync(1, Filial.FC, DayOfWeek.Wednesday)).Ok);
        Assert.False((await _padrao.CobrirAsync(4, Filial.Interiores, DayOfWeek.Wednesday)).Ok); // inativo
    }

    [Fact]
    public async Task Cobrir_RecusaNoDiaDeFolgaDoProprioCobridor()
    {
        await _padrao.DesalocarAsync(1, DayOfWeek.Wednesday); // a quarta vira folga da Ana

        var r = await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);

        Assert.False(r.Ok);
        Assert.Contains("folga", r.Erro);
        Assert.Empty((await DiaPadrao(Filial.Interiores, DayOfWeek.Wednesday)).Candidatos);
    }

    [Fact]
    public async Task DiaDeCobertura_NaoPodeSerAlocadoMovidoOuRemovidoPelaPropriaFilial()
    {
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);

        var alocar = await _padrao.AlocarAsync(1, DayOfWeek.Wednesday);
        Assert.False(alocar.Ok);
        Assert.Contains("cobrindo Interiores", alocar.Erro);
        Assert.False((await _padrao.MoverAsync(1, DayOfWeek.Monday, DayOfWeek.Wednesday)).Ok);
        Assert.False((await _padrao.DesalocarAsync(1, DayOfWeek.Wednesday)).Ok);
        Assert.False((await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday)).Ok); // já cobre

        // Alocar todos os dias não desfaz a cobertura.
        Assert.True((await _padrao.AlocarTodosOsDiasAsync(1)).Ok);
        Assert.Single((await DiaPadrao(Filial.Interiores, DayOfWeek.Wednesday)).Cobrindo);
    }

    [Fact]
    public async Task RemoverCobertura_VoltaParaAPropriaFilial()
    {
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);

        Assert.True((await _padrao.RemoverCoberturaAsync(1, DayOfWeek.Wednesday)).Ok);

        Assert.Empty((await DiaPadrao(Filial.Interiores, DayOfWeek.Wednesday)).Cobrindo);
        var matriz = await DiaPadrao(Filial.Matriz, DayOfWeek.Wednesday);
        Assert.Contains(1, matriz.Setores.SelectMany(s => s.Escalados).Select(f => f.Id));
        Assert.Empty(matriz.Fora);
        Assert.False((await _padrao.RemoverCoberturaAsync(1, DayOfWeek.Wednesday)).Ok);
    }

    // ───────────── Calendário ─────────────

    [Fact]
    public async Task Calendario_MostraCobridorNaFilialCoberta_eForaNaPropria()
    {
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);

        var interiores = await DiaCalendario(Filial.Interiores, Quarta);
        Assert.Equal([1], interiores.Cobrindo.Select(c => c.Id));
        Assert.Equal("Caixa", interiores.Cobrindo[0].Setor);
        Assert.Equal([3], interiores.Disponiveis.Select(f => f.Id)); // Ivo de folga
        Assert.DoesNotContain(interiores.Setores, s => s.AbaixoDoMinimo); // a Ana conta no Caixa da Interiores

        var matriz = await DiaCalendario(Filial.Matriz, Quarta);
        Assert.DoesNotContain(1, Escalados(matriz));
        Assert.DoesNotContain(matriz.Disponiveis, f => f.Id == 1);
        Assert.Equal([1], matriz.Fora.Select(f => f.Id));

        // Na semana seguinte também (modelo semanal); nos outros dias a Ana fica na Matriz.
        Assert.Equal([1], (await DiaCalendario(Filial.Interiores, Quarta.AddDays(7))).Cobrindo.Select(c => c.Id));
        Assert.Empty((await DiaCalendario(Filial.Interiores, Segunda)).Cobrindo);
        Assert.Contains(1, Escalados(await DiaCalendario(Filial.Matriz, Segunda)));
    }

    [Fact]
    public async Task Calendario_SemCobertura_FolgaDoFixoDeixaSetorAbaixoDoMinimo()
    {
        var interiores = await DiaCalendario(Filial.Interiores, Quarta);
        Assert.Contains(interiores.Setores, s => s.AbaixoDoMinimo);
        Assert.Empty(interiores.Cobrindo);
        Assert.DoesNotContain(interiores.Disponiveis, f => f.Id == 1); // quem só tem a filial como extra não aparece como folga
    }

    [Fact]
    public async Task Calendario_DiaFechadoNaFilialCoberta_SemCobertura()
    {
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);
        await _calendario.DefinirDiaFechadoAsync(Filial.Interiores, Quarta, true);

        Assert.Empty((await DiaCalendario(Filial.Interiores, Quarta)).Cobrindo);
        Assert.DoesNotContain((await _horas.ObterFuncionarioAsync(1, Quarta))!.Dias, d => d.Data == Quarta);
    }

    [Fact]
    public async Task ExcecaoRemovendoODia_TiraTambemACobertura()
    {
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);
        await using (var db = _fabrica.CreateDbContext())
        {
            db.Escalas.Add(new Escala { FuncionarioId = 1, Data = Quarta, Removido = true });
            await db.SaveChangesAsync();
        }

        Assert.Empty((await DiaCalendario(Filial.Interiores, Quarta)).Cobrindo);
        var matriz = await DiaCalendario(Filial.Matriz, Quarta);
        Assert.Contains(matriz.Disponiveis, f => f.Id == 1);
        Assert.Empty(matriz.Fora);
    }

    // ───────────── Cadastro ─────────────

    [Fact]
    public async Task Cadastro_FilialExtraIgualAPropria_eRecusada()
    {
        var funcs = new FuncionarioService(_fabrica);
        await Assert.ThrowsAsync<CadastroException>(() => funcs.SalvarAsync(new Funcionario
        {
            Id = 2, Nome = "Bia", Filial = Filial.Matriz, SetorId = 1, FilialExtra = Filial.Matriz,
        }));
    }

    [Fact]
    public async Task Cadastro_GravaFilialExtra_eTrocarDesfazAsCoberturas()
    {
        var funcs = new FuncionarioService(_fabrica);
        var bia = await funcs.SalvarAsync(new Funcionario { Id = 2, Nome = "Bia", Filial = Filial.Matriz, SetorId = 1, FilialExtra = Filial.FC });
        Assert.Equal(Filial.FC, bia.FilialExtra);
        Assert.Equal(Filial.FC, (await funcs.ListarAsync(Filial.Matriz)).Single(f => f.Id == 2).FilialExtra);

        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);
        await funcs.SalvarAsync(new Funcionario { Id = 1, Nome = "Ana", Filial = Filial.Matriz, SetorId = 1, FilialExtra = null });

        Assert.Empty((await DiaPadrao(Filial.Interiores, DayOfWeek.Wednesday)).Cobrindo);
        Assert.Contains(1, (await DiaPadrao(Filial.Matriz, DayOfWeek.Wednesday)).Setores.SelectMany(s => s.Escalados).Select(f => f.Id));
    }

    // ───────────── Horas, resumo e planilha ─────────────

    [Fact]
    public async Task Horas_DiaDeCoberturaContaNaSemanaDoCobridor()
    {
        await ExpedienteSegASab();
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);

        var h = (await _horas.ObterFuncionarioAsync(1, Segunda))!;

        Assert.Equal(6, h.Dias.Count);
        Assert.Equal(TimeSpan.FromHours(45), h.Totais.Total);
        Assert.Equal(Filial.Interiores, h.Dias.Single(d => d.Data == Quarta).Cobertura);
        Assert.All(h.Dias.Where(d => d.Data != Quarta), d => Assert.Null(d.Cobertura));
    }

    [Fact]
    public async Task Horas_VistoNaFilialDeOrigem_SemanaCompleta_eNaFilialExtra_SoOsDiasDeCobertura()
    {
        await ExpedienteSegASab();
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);

        var naMatriz = (await _horas.ObterFuncionarioAsync(1, Segunda, Filial.Matriz))!;
        Assert.Equal(6, naMatriz.Dias.Count);
        Assert.Equal(TimeSpan.FromHours(45), naMatriz.Totais.Total);

        var naInteriores = (await _horas.ObterFuncionarioAsync(1, Segunda, Filial.Interiores))!;
        var dia = Assert.Single(naInteriores.Dias);
        Assert.Equal((Quarta, (Filial?)Filial.Interiores), (dia.Data, dia.Cobertura));
        Assert.Equal(TimeSpan.FromHours(7.5), naInteriores.Totais.Total);

        // Filial que não é a dele nem a extra: semana completa (não há o que filtrar).
        Assert.Equal(6, (await _horas.ObterFuncionarioAsync(1, Segunda, Filial.FC))!.Dias.Count);
    }

    [Fact]
    public async Task Resumo_CobridorSoNaPropriaFilial_ComODiaMarcado()
    {
        await ExpedienteSegASab();
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);

        var resumo = await _horas.ObterResumoAsync(Segunda);

        var anaMatriz = resumo.Filiais.Single(f => f.Filial == Filial.Matriz).Funcionarios.Single(f => f.FuncionarioId == 1);
        Assert.Equal(TimeSpan.FromHours(45), anaMatriz.Totais.Total);
        Assert.Equal(Filial.Interiores, anaMatriz.Dias.Single(d => d.Data == Quarta).Cobertura);

        // A filial coberta não lista quem veio cobrir.
        Assert.Equal(["Ivo"], resumo.Filiais.Single(f => f.Filial == Filial.Interiores).Funcionarios.Select(f => f.Nome));

    }

    [Fact]
    public async Task Resumo_SemCobertura_NaoListaQuemSoTemAFilialComoExtra()
    {
        var resumo = await _horas.ObterResumoAsync(Segunda);
        Assert.Equal(["Ivo"], resumo.Filiais.Single(f => f.Filial == Filial.Interiores).Funcionarios.Select(f => f.Nome));
        Assert.Equal(["Ana", "Bia"], resumo.Filiais.Single(f => f.Filial == Filial.Matriz).Funcionarios.Select(f => f.Nome));
    }

    [Fact]
    public async Task Planilha_CobridorSoNaAbaDaPropriaFilial()
    {
        await ExpedienteSegASab();
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);
        var servico = new ExportacaoEscalaService(_padrao, _expedientes);

        using var wb = new XLWorkbook(new MemoryStream(await servico.GerarPlanilhaAsync()));

        var matriz = wb.Worksheet("Matriz");
        Assert.Equal("08:00–17:00 · Interiores", Celula(matriz, "Ana", "Quarta"));
        Assert.Equal("08:00–17:00", Celula(matriz, "Ana", "Segunda"));
        Assert.Equal("45h", Horas(matriz, "Ana"));

        var interiores = wb.Worksheet("Interiores");
        Assert.Equal("Descanso", Celula(interiores, "Ivo", "Quarta"));
        // Quem cobre não aparece na aba da filial coberta.
        Assert.DoesNotContain(interiores.Column(1).CellsUsed(), c => c.GetString() == "Ana");
    }

    [Fact]
    public async Task Planilha_FuncionarioCobrindoNaSegunda_AindaApareceNaAbaDele()
    {
        await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Wednesday);
        await using (var db = _fabrica.CreateDbContext())
        {
            // Ivo folga também na segunda, e a Ana cobre: na segunda ela não está escalada nem de folga na Matriz.
            db.EscalasPadrao.RemoveRange(db.EscalasPadrao.Where(p => p.FuncionarioId == 3 && p.DiaSemana == DayOfWeek.Monday));
            await db.SaveChangesAsync();
        }
        Assert.True((await _padrao.CobrirAsync(1, Filial.Interiores, DayOfWeek.Monday)).Ok);
        var servico = new ExportacaoEscalaService(_padrao, _expedientes);

        using var wb = new XLWorkbook(new MemoryStream(await servico.GerarPlanilhaAsync()));

        Assert.Equal("Escalado · Interiores", Celula(wb.Worksheet("Matriz"), "Ana", "Segunda"));
    }

    private static IXLRow Linha(IXLWorksheet ws, string funcionario) =>
        ws.Row(ws.Column(1).CellsUsed().First(c => c.GetString() == funcionario).Address.RowNumber);

    private static int Coluna(IXLWorksheet ws, string cabecalho) =>
        ws.Row(1).CellsUsed().First(c => c.GetString() == cabecalho).Address.ColumnNumber;

    private static string Horas(IXLWorksheet ws, string funcionario) =>
        Linha(ws, funcionario).Cell(Coluna(ws, "Horas na semana")).GetString();

    private static string Celula(IXLWorksheet ws, string funcionario, string dia) =>
        Linha(ws, funcionario).Cell(Coluna(ws, dia)).GetString();

    private sealed class RelogioFixo(DateOnly hoje) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(hoje.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
