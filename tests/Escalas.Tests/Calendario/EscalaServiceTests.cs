using Escalas.Application.Calendario;
using Escalas.Application.Feriados;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Calendario;
using Escalas.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Tests.Calendario;

public sealed class EscalaServiceTests : IDisposable
{
    private static readonly DateOnly Segunda = new(2026, 10, 12);
    private static readonly DateOnly Terca = Segunda.AddDays(1);
    private static readonly DateOnly Quarta = Segunda.AddDays(2);

    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly FabricaTeste _fabrica;
    private readonly FeriadosFake _feriados = new();
    private readonly EscalaService _servico;

    public EscalaServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _conexao.Open();
        _fabrica = new FabricaTeste(_conexao);
        using (var db = _fabrica.CreateDbContext()) db.Database.EnsureCreated();
        _servico = new EscalaService(_fabrica, _feriados);
        Semear();
    }

    public void Dispose() => _conexao.Dispose();

    // Matriz: setor 1 (Caixa, mín 2) com Ana(1), Bia(2); setor 2 (Estoque, mín 1) com Caio(3), Dora(4, inativa).
    // FC: setor 3 com Eva(5).
    private void Semear()
    {
        using var db = _fabrica.CreateDbContext();
        db.Setores.AddRange(
            new Setor { Id = 1, Filial = Filial.Matriz, Nome = "Caixa", MinimoFuncionarios = 2 },
            new Setor { Id = 2, Filial = Filial.Matriz, Nome = "Estoque", MinimoFuncionarios = 1 },
            new Setor { Id = 3, Filial = Filial.FC, Nome = "Salão", MinimoFuncionarios = 1 });
        db.Funcionarios.AddRange(
            new Funcionario { Id = 1, Filial = Filial.Matriz, SetorId = 1, Nome = "Ana" },
            new Funcionario { Id = 2, Filial = Filial.Matriz, SetorId = 1, Nome = "Bia" },
            new Funcionario { Id = 3, Filial = Filial.Matriz, SetorId = 2, Nome = "Caio" },
            new Funcionario { Id = 4, Filial = Filial.Matriz, SetorId = 2, Nome = "Dora", Ativo = false },
            new Funcionario { Id = 5, Filial = Filial.FC, SetorId = 3, Nome = "Eva" });
        db.SaveChanges();
    }

    /// <summary>Grava uma inclusão pontual direto no banco (o calendário não oferece mais essa ação).</summary>
    private void Escalar(int funcionarioId, DateOnly data)
    {
        using var db = _fabrica.CreateDbContext();
        db.Escalas.Add(new Escala { FuncionarioId = funcionarioId, Data = data });
        db.SaveChanges();
    }

    [Fact]
    public async Task Domingo_VemFechadoPorPadrao_ePodeSerReaberto()
    {
        var domingo = Segunda.AddDays(6);
        Escalar(1, domingo);

        var dia = (await _servico.ObterSemanaAsync(Filial.Matriz, Segunda)).Dias[6];
        Assert.True(dia.Fechado);
        Assert.All(dia.Setores, s => Assert.Empty(s.Escalados));

        await _servico.DefinirDiaFechadoAsync(Filial.Matriz, domingo, false);

        dia = (await _servico.ObterSemanaAsync(Filial.Matriz, Segunda)).Dias[6];
        Assert.False(dia.Fechado);
        Assert.Single(dia.Setores.Single(s => s.SetorId == 1).Escalados);
    }

    [Fact]
    public async Task Foto_ChegaNosDtosDaSemana()
    {
        await using (var db = _fabrica.CreateDbContext())
        {
            db.Funcionarios.Find(1)!.Foto = [1, 2, 3];
            db.SaveChanges();
        }
        Escalar(1, Segunda);

        var dia = (await _servico.ObterSemanaAsync(Filial.Matriz, Segunda)).Dias[0];
        Assert.Equal(new byte[] { 1, 2, 3 }, dia.Setores.SelectMany(x => x.Escalados).Single(f => f.Id == 1).Foto);
        Assert.Null(dia.Disponiveis.First(f => f.Id == 2).Foto);
    }

    [Fact]
    public async Task ObterSemana_RetornaSeteDiasComecandoNaSegunda()
    {
        var semana = await _servico.ObterSemanaAsync(Filial.Matriz, Quarta);

        Assert.Equal(Segunda, semana.Inicio);
        Assert.Equal(7, semana.Dias.Count);
        Assert.Equal(Segunda.AddDays(6), semana.Dias[^1].Data);
    }

    [Fact]
    public async Task Escalados_AtualizamContagemEAlertaDoSetor()
    {
        Escalar(1, Segunda);

        var dia = (await _servico.ObterSemanaAsync(Filial.Matriz, Segunda)).Dias[0];
        var caixa = dia.Setores.Single(s => s.SetorId == 1);

        Assert.Single(caixa.Escalados);
        Assert.True(caixa.AbaixoDoMinimo); // 1 de 2

        Escalar(2, Segunda);
        dia = (await _servico.ObterSemanaAsync(Filial.Matriz, Segunda)).Dias[0];
        Assert.False(dia.Setores.Single(s => s.SetorId == 1).AbaixoDoMinimo);
    }

    [Fact]
    public async Task Disponiveis_ExcluemEscaladosEInativos()
    {
        Escalar(1, Segunda);

        var dia = (await _servico.ObterSemanaAsync(Filial.Matriz, Segunda)).Dias[0];

        Assert.Equal(["Bia", "Caio"], dia.Disponiveis.Select(f => f.Nome).ToArray());
    }

    [Fact]
    public async Task DiaFechado_AparecaComoFechadoESemAlerta()
    {
        await _servico.DefinirDiaFechadoAsync(Filial.Matriz, Segunda, true);

        var dia = (await _servico.ObterSemanaAsync(Filial.Matriz, Segunda)).Dias[0];

        Assert.Equal(StatusDia.Fechado, dia.Status);
        Assert.All(dia.Setores, s => Assert.False(s.AbaixoDoMinimo));
    }

    [Fact]
    public async Task ReabrirDia_VoltaAoNormalEGeraAlerta()
    {
        await _servico.DefinirDiaFechadoAsync(Filial.Matriz, Segunda, true);
        await _servico.DefinirDiaFechadoAsync(Filial.Matriz, Segunda, false);

        var dia = (await _servico.ObterSemanaAsync(Filial.Matriz, Segunda)).Dias[0];

        Assert.Equal(StatusDia.Normal, dia.Status);
        Assert.Contains(dia.Setores, s => s.AbaixoDoMinimo);
    }

    [Fact]
    public async Task Feriado_VemFechadoPorPadraoComNome_ePodeSerReaberto()
    {
        _feriados.Itens.Add(new Feriado { Data = Quarta, Nome = "Dia Teste", Abrangencia = AbrangenciaFeriado.Nacional });
        Escalar(1, Quarta);

        var semana = await _servico.ObterSemanaAsync(Filial.Matriz, Segunda);
        Assert.Equal(StatusDia.Fechado, semana.Dias[2].Status);
        Assert.Equal("Dia Teste", semana.Dias[2].NomeFeriado);
        Assert.All(semana.Dias[2].Setores, s => Assert.Empty(s.Escalados));

        await _servico.DefinirDiaFechadoAsync(Filial.Matriz, Quarta, false);
        semana = await _servico.ObterSemanaAsync(Filial.Matriz, Segunda);
        Assert.Equal(StatusDia.Feriado, semana.Dias[2].Status);
        Assert.Equal("Dia Teste", semana.Dias[2].NomeFeriado);
        Assert.Single(semana.Dias[2].Setores.Single(s => s.SetorId == 1).Escalados);
    }

    [Fact]
    public async Task DiaFechado_ValeSoParaAFilialEscolhida()
    {
        await _servico.DefinirDiaFechadoAsync(Filial.FC, Segunda, true);

        var semana = await _servico.ObterSemanaAsync(Filial.Matriz, Segunda);

        Assert.Equal(StatusDia.Normal, semana.Dias[0].Status);
    }

    internal sealed class FeriadosFake : IFeriadoConsulta
    {
        public List<Feriado> Itens { get; } = [];

        public Task<IReadOnlyList<Feriado>> ObterAsync(Filial filial, DateOnly inicio, DateOnly fim, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Feriado>>(Itens.Where(f => f.Data >= inicio && f.Data <= fim).ToList());
    }

    internal sealed class FabricaTeste(SqliteConnection conexao) : IDbContextFactory<EscalasDbContext>
    {
        public EscalasDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<EscalasDbContext>().UseSqlite(conexao).Options);
    }
}
