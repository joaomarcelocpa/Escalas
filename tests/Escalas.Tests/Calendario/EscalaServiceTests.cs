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

    // Filial 1: setor 1 (Caixa, mín 2) com Ana(1), Bia(2); setor 2 (Estoque, mín 1) com Caio(3), Dora(4, inativa).
    // Filial 2: setor 3 com Eva(5).
    private void Semear()
    {
        using var db = _fabrica.CreateDbContext();
        db.Filiais.AddRange(
            new Filial { Id = 1, Nome = "Centro", Cidade = "Recife", UF = "PE" },
            new Filial { Id = 2, Nome = "Praia", Cidade = "Olinda", UF = "PE" });
        db.Setores.AddRange(
            new Setor { Id = 1, FilialId = 1, Nome = "Caixa", MinimoFuncionarios = 2 },
            new Setor { Id = 2, FilialId = 1, Nome = "Estoque", MinimoFuncionarios = 1 },
            new Setor { Id = 3, FilialId = 2, Nome = "Salão", MinimoFuncionarios = 1 });
        db.Funcionarios.AddRange(
            new Funcionario { Id = 1, FilialId = 1, SetorId = 1, Nome = "Ana" },
            new Funcionario { Id = 2, FilialId = 1, SetorId = 1, Nome = "Bia" },
            new Funcionario { Id = 3, FilialId = 1, SetorId = 2, Nome = "Caio" },
            new Funcionario { Id = 4, FilialId = 1, SetorId = 2, Nome = "Dora", Ativo = false },
            new Funcionario { Id = 5, FilialId = 2, SetorId = 3, Nome = "Eva" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Domingo_VemFechadoPorPadrao_ePodeSerReaberto()
    {
        var domingo = Segunda.AddDays(6);

        var dia = (await _servico.ObterSemanaAsync(1, Segunda)).Dias[6];
        Assert.True(dia.Fechado);
        Assert.False((await _servico.EscalarAsync(1, domingo)).Ok);

        await _servico.DefinirDiaFechadoAsync(1, domingo, false);

        Assert.False((await _servico.ObterSemanaAsync(1, Segunda)).Dias[6].Fechado);
        Assert.True((await _servico.EscalarAsync(1, domingo)).Ok);
    }

    [Fact]
    public async Task Foto_ChegaNosDtosDaSemana()
    {
        await using (var db = _fabrica.CreateDbContext())
        {
            db.Funcionarios.Find(1)!.Foto = [1, 2, 3];
            db.SaveChanges();
        }
        await _servico.EscalarAsync(1, Segunda);

        var dia = (await _servico.ObterSemanaAsync(1, Segunda)).Dias[0];
        Assert.Equal(new byte[] { 1, 2, 3 }, dia.Setores.SelectMany(x => x.Escalados).Single(f => f.Id == 1).Foto);
        Assert.Null(dia.Disponiveis.First(f => f.Id == 2).Foto);
    }

    [Fact]
    public async Task ObterSemana_RetornaSeteDiasComecandoNaSegunda()
    {
        var semana = await _servico.ObterSemanaAsync(1, Quarta);

        Assert.Equal(Segunda, semana.Inicio);
        Assert.Equal(7, semana.Dias.Count);
        Assert.Equal(Segunda.AddDays(6), semana.Dias[^1].Data);
    }

    [Fact]
    public async Task Escalar_AtualizaContagemEAlertaDoSetor()
    {
        await _servico.EscalarAsync(1, Segunda);

        var dia = (await _servico.ObterSemanaAsync(1, Segunda)).Dias[0];
        var caixa = dia.Setores.Single(s => s.SetorId == 1);

        Assert.Single(caixa.Escalados);
        Assert.True(caixa.AbaixoDoMinimo); // 1 de 2

        await _servico.EscalarAsync(2, Segunda);
        dia = (await _servico.ObterSemanaAsync(1, Segunda)).Dias[0];
        Assert.False(dia.Setores.Single(s => s.SetorId == 1).AbaixoDoMinimo);
    }

    [Fact]
    public async Task Disponiveis_ExcluemEscaladosEInativos()
    {
        await _servico.EscalarAsync(1, Segunda);

        var dia = (await _servico.ObterSemanaAsync(1, Segunda)).Dias[0];

        Assert.Equal(["Bia", "Caio"], dia.Disponiveis.Select(f => f.Nome).ToArray());
    }

    [Fact]
    public async Task Escalar_DuasVezesNoMesmoDia_Falha()
    {
        await _servico.EscalarAsync(1, Segunda);
        var r = await _servico.EscalarAsync(1, Segunda);

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Escalar_FuncionarioInativo_Falha()
    {
        Assert.False((await _servico.EscalarAsync(4, Segunda)).Ok);
    }

    [Fact]
    public async Task Mover_TransfereDeUmDiaParaOutro()
    {
        await _servico.EscalarAsync(1, Segunda);

        var r = await _servico.MoverAsync(1, Segunda, Terca);

        Assert.True(r.Ok);
        var semana = await _servico.ObterSemanaAsync(1, Segunda);
        Assert.Empty(semana.Dias[0].Setores.Single(s => s.SetorId == 1).Escalados);
        Assert.Single(semana.Dias[1].Setores.Single(s => s.SetorId == 1).Escalados);
    }

    [Fact]
    public async Task Mover_ParaDiaFechado_FalhaEMantemOrigem()
    {
        await _servico.EscalarAsync(1, Segunda);
        await _servico.DefinirDiaFechadoAsync(1, Terca, true);

        var r = await _servico.MoverAsync(1, Segunda, Terca);

        Assert.False(r.Ok);
        var semana = await _servico.ObterSemanaAsync(1, Segunda);
        Assert.Single(semana.Dias[0].Setores.Single(s => s.SetorId == 1).Escalados);
    }

    [Fact]
    public async Task Mover_ParaDiaOndeJaEstaEscalado_Falha()
    {
        await _servico.EscalarAsync(1, Segunda);
        await _servico.EscalarAsync(1, Terca);

        Assert.False((await _servico.MoverAsync(1, Segunda, Terca)).Ok);
    }

    [Fact]
    public async Task Mover_SemEstarEscaladoNaOrigem_Falha()
    {
        Assert.False((await _servico.MoverAsync(1, Segunda, Terca)).Ok);
    }

    [Fact]
    public async Task Remover_TiraDaEscala()
    {
        await _servico.EscalarAsync(1, Segunda);

        Assert.True((await _servico.RemoverAsync(1, Segunda)).Ok);
        Assert.False((await _servico.RemoverAsync(1, Segunda)).Ok);
    }

    [Fact]
    public async Task DiaFechado_AparecaComoFechadoESemAlerta()
    {
        await _servico.DefinirDiaFechadoAsync(1, Segunda, true);

        var dia = (await _servico.ObterSemanaAsync(1, Segunda)).Dias[0];

        Assert.Equal(StatusDia.Fechado, dia.Status);
        Assert.All(dia.Setores, s => Assert.False(s.AbaixoDoMinimo));
    }

    [Fact]
    public async Task ReabrirDia_VoltaAoNormalEGeraAlerta()
    {
        await _servico.DefinirDiaFechadoAsync(1, Segunda, true);
        await _servico.DefinirDiaFechadoAsync(1, Segunda, false);

        var dia = (await _servico.ObterSemanaAsync(1, Segunda)).Dias[0];

        Assert.Equal(StatusDia.Normal, dia.Status);
        Assert.Contains(dia.Setores, s => s.AbaixoDoMinimo);
    }

    [Fact]
    public async Task Feriado_MarcaStatusComNome_EFechadoPrevalece()
    {
        _feriados.Itens.Add(new Feriado { Data = Quarta, Nome = "Dia Teste", Abrangencia = AbrangenciaFeriado.Nacional });

        var semana = await _servico.ObterSemanaAsync(1, Segunda);
        Assert.Equal(StatusDia.Feriado, semana.Dias[2].Status);
        Assert.Equal("Dia Teste", semana.Dias[2].NomeFeriado);

        await _servico.DefinirDiaFechadoAsync(1, Quarta, true);
        semana = await _servico.ObterSemanaAsync(1, Segunda);
        Assert.Equal(StatusDia.Fechado, semana.Dias[2].Status);
        Assert.Equal("Dia Teste", semana.Dias[2].NomeFeriado);
    }

    [Fact]
    public async Task DiaFechado_ValeSoParaAFilialEscolhida()
    {
        await _servico.DefinirDiaFechadoAsync(2, Segunda, true);

        var semana = await _servico.ObterSemanaAsync(1, Segunda);

        Assert.Equal(StatusDia.Normal, semana.Dias[0].Status);
    }

    internal sealed class FeriadosFake : IFeriadoConsulta
    {
        public List<Feriado> Itens { get; } = [];

        public Task<IReadOnlyList<Feriado>> ObterAsync(int filialId, DateOnly inicio, DateOnly fim, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Feriado>>(Itens.Where(f => f.Data >= inicio && f.Data <= fim).ToList());
    }

    internal sealed class FabricaTeste(SqliteConnection conexao) : IDbContextFactory<EscalasDbContext>
    {
        public EscalasDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<EscalasDbContext>().UseSqlite(conexao).Options);
    }
}
