using ClosedXML.Excel;
using Escalas.Application.Cadastros;
using Escalas.Application.Calendario;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Cadastros;
using Escalas.Infrastructure.Calendario;
using Escalas.Infrastructure.Exportacao;
using Microsoft.Data.Sqlite;

namespace Escalas.Tests.Calendario;

/// <summary>Horário de trabalho personalizado por funcionário (cadastro), no lugar do expediente geral.</summary>
public sealed class HorarioPersonalizadoTests : IDisposable
{
    private static readonly DateOnly Segunda = new(2026, 10, 12);

    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly EscalaServiceTests.FabricaTeste _fabrica;
    private readonly ExpedienteService _expedientes;
    private readonly FuncionarioService _funcionarios;
    private readonly HorasService _horas;

    public HorarioPersonalizadoTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _conexao.Open();
        _fabrica = new EscalaServiceTests.FabricaTeste(_conexao);
        using (var db = _fabrica.CreateDbContext()) db.Database.EnsureCreated();
        _expedientes = new ExpedienteService(_fabrica);
        _funcionarios = new FuncionarioService(_fabrica);
        _horas = new HorasService(_fabrica, new EscalaService(_fabrica, new EscalaServiceTests.FeriadosFake()), _expedientes,
            new RelogioFixo(Segunda.AddDays(2)));

        // Matriz: Ana(1) e Bia(2) trabalham seg, ter e sáb. Expediente geral 08:00–18:00 (10h → 8h30) de segunda a sábado.
        using var d = _fabrica.CreateDbContext();
        d.Setores.Add(new Setor { Id = 1, Filial = Filial.Matriz, Nome = "Caixa" });
        d.Funcionarios.AddRange(
            new Funcionario { Id = 1, Filial = Filial.Matriz, SetorId = 1, Nome = "Ana" },
            new Funcionario { Id = 2, Filial = Filial.Matriz, SetorId = 1, Nome = "Bia" });
        foreach (var id in new[] { 1, 2 })
        foreach (var dia in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Saturday })
            d.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = id, DiaSemana = dia });
        d.SaveChanges();
        _expedientes.SalvarAsync(ExpedienteSemanaGeral()).GetAwaiter().GetResult();
    }

    public void Dispose() => _conexao.Dispose();

    private static List<ExpedienteEntrada> ExpedienteSemanaGeral() =>
        [.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }
            .Select(d => new ExpedienteEntrada(d, new TimeOnly(8, 0), new TimeOnly(18, 0)))];

    private static HorarioFuncionario H(DayOfWeek dia, int inicio, int fim) =>
        new() { DiaSemana = dia, Inicio = new TimeOnly(inicio, 0), Fim = new TimeOnly(fim, 0) };

    private async Task<Funcionario> Ana() => (await _funcionarios.ListarAsync(Filial.Matriz)).Single(f => f.Id == 1);

    private async Task SalvarAna(bool ativo, params HorarioFuncionario[] horarios)
    {
        var ana = await Ana();
        ana.HorarioPersonalizado = ativo;
        ana.Horarios = [.. horarios];
        await _funcionarios.SalvarAsync(ana);
    }

    [Fact]
    public void Tabela_PersonalizadoNoDia_SenaoExpedienteGeral()
    {
        var geral = new Dictionary<DayOfWeek, HorarioDia> { [DayOfWeek.Monday] = new(new(8, 0), new(18, 0)) };
        var pers = new Dictionary<int, IReadOnlyDictionary<DayOfWeek, HorarioDia>>
        {
            [1] = new Dictionary<DayOfWeek, HorarioDia> { [DayOfWeek.Sunday] = new(new(9, 0), new(13, 0)) },
        };
        var t = new TabelaHorarios(geral, pers);

        Assert.Equal(new HorarioDia(new(8, 0), new(18, 0)), t.Do(1, DayOfWeek.Monday));   // dia sem personalizado
        Assert.Equal(new HorarioDia(new(9, 0), new(13, 0)), t.Do(1, DayOfWeek.Sunday));   // personalizado vale mesmo sem expediente geral
        Assert.Null(t.Do(2, DayOfWeek.Sunday));
        Assert.True(t.Dia(1, Segunda.AddDays(6)).Personalizado);
        Assert.False(t.Dia(1, Segunda).Personalizado);
    }

    [Fact]
    public async Task Horas_UsamHorarioPersonalizado_eExpedienteGeralNosOutrosDias()
    {
        await SalvarAna(true, H(DayOfWeek.Monday, 10, 14), H(DayOfWeek.Saturday, 7, 16));

        var ana = (await _horas.ObterFuncionarioAsync(1, Segunda))!;

        Assert.Equal([new TimeOnly(10, 0), new TimeOnly(8, 0), new TimeOnly(7, 0)], ana.Dias.Select(d => d.Inicio!.Value));
        Assert.Equal([true, false, true], ana.Dias.Select(d => d.Personalizado));
        Assert.False(ana.Dias[0].DescontaAlmoco); // 4h: sem almoço
        Assert.True(ana.Dias[2].DescontaAlmoco);  // 9h: −1h30
        Assert.Equal(TimeSpan.FromHours(4 + 8.5 + 7.5), ana.Totais.Total);

        var bia = (await _horas.ObterFuncionarioAsync(2, Segunda))!;
        Assert.Equal(TimeSpan.FromHours(3 * 8.5), bia.Totais.Total); // não é afetada
    }

    [Fact]
    public async Task Resumo_UsaHorarioPersonalizado()
    {
        await SalvarAna(true, H(DayOfWeek.Tuesday, 12, 18));

        var matriz = (await _horas.ObterResumoAsync(Segunda)).Filiais.Single(f => f.Filial == Filial.Matriz);
        var ana = matriz.Funcionarios.Single(f => f.Nome == "Ana");

        Assert.Equal(new TimeOnly(12, 0), ana.Dias.Single(d => d.Data == Segunda.AddDays(1)).Inicio);
        Assert.Equal(TimeSpan.FromHours(8.5 + 6 + 8.5), ana.Totais.Total);
    }

    [Fact]
    public async Task OpcaoDesativada_IgnoraHorariosGuardados_eReativarVoltaAValer()
    {
        await SalvarAna(true, H(DayOfWeek.Monday, 10, 14));
        var ana = await Ana();
        ana.HorarioPersonalizado = false;
        ana.Horarios = [];
        await _funcionarios.SalvarAsync(ana);

        Assert.Equal(TimeSpan.FromHours(3 * 8.5), (await _horas.ObterFuncionarioAsync(1, Segunda))!.Totais.Total);
        var guardada = await Ana();
        Assert.False(guardada.HorarioPersonalizado);
        Assert.Single(guardada.Horarios); // continua guardado

        guardada.HorarioPersonalizado = true;
        await _funcionarios.SalvarAsync(guardada);
        Assert.Equal(TimeSpan.FromHours(4 + 2 * 8.5), (await _horas.ObterFuncionarioAsync(1, Segunda))!.Totais.Total);
    }

    [Fact]
    public async Task Salvar_TrocaOsDias()
    {
        await SalvarAna(true, H(DayOfWeek.Monday, 10, 14), H(DayOfWeek.Tuesday, 10, 14));
        await SalvarAna(true, H(DayOfWeek.Tuesday, 9, 15), H(DayOfWeek.Friday, 9, 15));

        var horarios = (await Ana()).Horarios.OrderBy(h => h.DiaSemana).ToList();
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Friday], horarios.Select(h => h.DiaSemana));
        Assert.All(horarios, h => Assert.Equal(new TimeOnly(9, 0), h.Inicio));
    }

    [Fact]
    public async Task Salvar_Valida()
    {
        var fimAntes = await Assert.ThrowsAsync<CadastroException>(() => SalvarAna(true, H(DayOfWeek.Monday, 14, 10)));
        Assert.StartsWith("Segunda:", fimAntes.Message);
        await Assert.ThrowsAsync<CadastroException>(() => SalvarAna(true));
        await Assert.ThrowsAsync<CadastroException>(() => SalvarAna(true, H(DayOfWeek.Monday, 8, 10), H(DayOfWeek.Monday, 9, 12)));
        await SalvarAna(false); // desativada não exige horários
    }

    [Fact]
    public async Task ExcluirFuncionario_RemoveOsHorarios()
    {
        await SalvarAna(true, H(DayOfWeek.Monday, 10, 14));
        await _funcionarios.ExcluirAsync(1);

        await using var db = _fabrica.CreateDbContext();
        Assert.Empty(db.HorariosFuncionario);
    }

    [Fact]
    public async Task Planilha_MostraHorarioPersonalizado()
    {
        await SalvarAna(true, H(DayOfWeek.Monday, 10, 14));
        var planilha = new ExportacaoEscalaService(new EscalaPadraoService(_fabrica), _expedientes);

        using var wb = new XLWorkbook(new MemoryStream(await planilha.GerarPlanilhaAsync()));
        var ws = wb.Worksheet("Matriz");
        var linhaAna = ws.Column(1).CellsUsed().First(c => c.GetString() == "Ana").Address.RowNumber;
        var linhaBia = ws.Column(1).CellsUsed().First(c => c.GetString() == "Bia").Address.RowNumber;

        Assert.Equal("10:00–14:00", ws.Cell(linhaAna, 3).GetString()); // segunda
        Assert.Equal("08:00–18:00", ws.Cell(linhaAna, 4).GetString()); // terça: geral
        Assert.Equal("21h", ws.Cell(linhaAna, 9).GetString());          // 4 + 8h30 + 8h30
        Assert.Equal("08:00–18:00", ws.Cell(linhaBia, 3).GetString());
    }

    private sealed class RelogioFixo(DateOnly hoje) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(hoje.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
