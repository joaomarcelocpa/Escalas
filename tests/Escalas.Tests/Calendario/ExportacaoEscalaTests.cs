using ClosedXML.Excel;
using Escalas.Application.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Calendario;
using Escalas.Infrastructure.Exportacao;
using Microsoft.Data.Sqlite;

namespace Escalas.Tests.Calendario;

public sealed class ExportacaoEscalaTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly EscalaServiceTests.FabricaTeste _fabrica;
    private readonly ExpedienteService _expedientes;
    private readonly ExportacaoEscalaService _servico;

    public ExportacaoEscalaTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _conexao.Open();
        _fabrica = new EscalaServiceTests.FabricaTeste(_conexao);
        using (var db = _fabrica.CreateDbContext()) db.Database.EnsureCreated();
        _expedientes = new ExpedienteService(_fabrica);
        _servico = new ExportacaoEscalaService(new EscalaPadraoService(_fabrica), _expedientes);

        using var d = _fabrica.CreateDbContext();
        d.Setores.AddRange(
            new Setor { Id = 1, Filial = Filial.Matriz, Nome = "Caixa" },
            new Setor { Id = 2, Filial = Filial.FC, Nome = "Salão" });
        d.Funcionarios.AddRange(
            new Funcionario { Id = 1, Filial = Filial.Matriz, SetorId = 1, Nome = "Ana" },
            new Funcionario { Id = 2, Filial = Filial.Matriz, SetorId = 1, Nome = "Bia" },
            new Funcionario { Id = 3, Filial = Filial.Matriz, SetorId = 1, Nome = "Dora", Ativo = false },
            new Funcionario { Id = 4, Filial = Filial.FC, SetorId = 2, Nome = "Eva" });
        d.EscalasPadrao.AddRange(
            new EscalaPadrao { FuncionarioId = 1, DiaSemana = DayOfWeek.Monday },
            new EscalaPadrao { FuncionarioId = 1, DiaSemana = DayOfWeek.Saturday },
            new EscalaPadrao { FuncionarioId = 2, DiaSemana = DayOfWeek.Monday },
            new EscalaPadrao { FuncionarioId = 4, DiaSemana = DayOfWeek.Tuesday });
        d.SaveChanges();
    }

    public void Dispose() => _conexao.Dispose();

    private async Task<XLWorkbook> Gerar() => new(new MemoryStream(await _servico.GerarPlanilhaAsync()));

    private static IXLCell Achar(IXLWorksheet ws, string texto) =>
        ws.CellsUsed().First(c => c.GetString() == texto);

    [Fact]
    public async Task UmaAbaPorFilial_NaOrdemFixa()
    {
        using var wb = await Gerar();
        Assert.Equal(Filiais.Todas.Select(f => f.Nome), wb.Worksheets.Select(w => w.Name));
    }

    private static string Celula(IXLWorksheet ws, string funcionario, string dia)
    {
        var linha = ws.Column(1).CellsUsed().First(c => c.GetString() == funcionario).Address.RowNumber;
        var col = ws.Row(1).CellsUsed().First(c => c.GetString() == dia).Address.ColumnNumber;
        return ws.Cell(linha, col).GetString();
    }

    [Fact]
    public async Task Aba_FuncionariosPorLinhaDiasPorColuna()
    {
        using var wb = await Gerar();
        var ws = wb.Worksheet("Matriz");
        Assert.Equal(["Funcionário", "Setor", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Horas na semana"],
            ws.Row(1).CellsUsed().Select(c => c.GetString()));
        Assert.Equal(["Ana", "Bia"], ws.Column(1).CellsUsed().Skip(1).Select(c => c.GetString()));
        Assert.Equal("Caixa", ws.Cell(2, 2).GetString());
        Assert.DoesNotContain(ws.CellsUsed(), c => c.GetString().Contains("Dora")); // inativa
        Assert.DoesNotContain(ws.CellsUsed(), c => c.GetString().Contains("Eva")); // outra filial
    }

    [Fact]
    public async Task Celula_HorarioDoExpediente_DescansoNaFolga()
    {
        await _expedientes.SalvarAsync([
            new ExpedienteEntrada(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 30)),
        ]);

        using var wb = await Gerar();
        var ws = wb.Worksheet("Matriz");
        Assert.Equal("08:00–17:30", Celula(ws, "Ana", "Segunda"));
        Assert.Equal("Escalado", Celula(ws, "Ana", "Sábado")); // trabalha, sábado sem expediente
        Assert.Equal("Descanso", Celula(ws, "Bia", "Sábado"));   // folga
        Assert.Equal("Descanso", Celula(ws, "Ana", "Terça"));
        Assert.DoesNotContain(ws.CellsUsed(), c => c.GetString().Contains("almoço", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ws.CellsUsed(), c => c.GetString().Contains("Total"));
    }

    [Fact]
    public async Task HorasNaSemana_DescontaAlmocoNosDiasDe8hOuMais()
    {
        await _expedientes.SalvarAsync([
            new ExpedienteEntrada(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 30)),   // 9h30 − 1h30 = 8h
            new ExpedienteEntrada(DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(12, 0)),  // 4h, sem almoço
        ]);

        using var wb = await Gerar();
        var ws = wb.Worksheet("Matriz");
        var col = ws.Row(1).CellsUsed().First(c => c.GetString() == "Horas na semana").Address.ColumnNumber;
        string Horas(string nome) => ws.Cell(ws.Column(1).CellsUsed().First(c => c.GetString() == nome).Address.RowNumber, col)
            .GetString();

        Assert.Equal("12h", Horas("Ana")); // segunda + sábado
        Assert.Equal("8h", Horas("Bia"));  // só segunda
        // Só horas e minutos, sem segundos: texto, não célula de duração.
        Assert.Equal(XLDataType.Text, ws.Cell(2, col).DataType);
    }

    [Fact]
    public async Task FilialSemFuncionarios_SoCabecalho()
    {
        using var wb = await Gerar();
        Assert.Equal(1, wb.Worksheet("Interiores").LastRowUsed()!.RowNumber());
    }
}
