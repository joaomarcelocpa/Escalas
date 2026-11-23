using Escalas.Application.Nucleo;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Modulos;
using Escalas.Infrastructure.Nucleo;
using Escalas.Infrastructure.Persistencia;
using Escalas.Infrastructure.Persistencia.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Tests.Nucleo;

/// <summary>Conversão de bancos com filiais cadastradas para as filiais fixas.</summary>
public class FiliaisFixasMigracaoTests
{
    private const string AntesDasFiliaisFixas = "20261119123922_RemoveUsuarios";

    private sealed class Cenario : IDisposable
    {
        public AmbienteTemp Amb { get; } = new();
        public CaminhosApp Caminhos { get; }
        public string Chave { get; } = BancoFabrica.GerarChave();

        public Cenario()
        {
            Directory.CreateDirectory(Amb.Dados);
            Caminhos = Amb.CaminhosCliente();
        }

        public EscalasDbContext Abrir() => new(BancoFabrica.Opcoes(Caminhos.CaminhoBanco, Chave));

        /// <summary>Cria o banco no esquema anterior (com tabela Filiais) e executa os comandos de semente.</summary>
        public async Task CriarBancoAntigoAsync(params string[] sql)
        {
            await using (var db = Abrir())
            {
                await db.GetService<IMigrator>().MigrateAsync(AntesDasFiliaisFixas);
                foreach (var s in sql) await db.Database.ExecuteSqlRawAsync(s);
            }
            SqliteConnection.ClearAllPools();
        }

        public async Task<ResultadoInicializacao> InicializarAsync()
        {
            await using var sp = new ServiceCollection().AdicionarNucleo(Caminhos).BuildServiceProvider();
            sp.GetRequiredService<IChaveStore>().Salvar(Chave);
            var r = await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync();
            SqliteConnection.ClearAllPools();
            return r;
        }

        public async Task<List<object?[]>> ConsultarAsync(string sql)
        {
            await using var db = Abrir();
            var c = db.Database.GetDbConnection();
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            var linhas = new List<object?[]>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var v = new object?[r.FieldCount];
                for (var i = 0; i < v.Length; i++) v[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                linhas.Add(v);
            }
            return linhas;
        }

        public async Task<long> ContarAsync(string sql) => (long)(await ConsultarAsync(sql))[0][0]!;

        public void Dispose() => Amb.Dispose();
    }

    private static async Task AssertProntoAsync(Cenario c)
    {
        var r = await c.InicializarAsync();
        Assert.True(r.Situacao == SituacaoBanco.Pronto, r.Mensagem);
    }

    private static readonly string[] SementeCompleta =
    [
        "INSERT INTO Filiais (Id, Nome, Cidade, UF) VALUES (10, '  MATRIZ ', 'Sete Lagoas', 'MG'), (20, 'Depósito', 'Sete Lagoas', 'MG'), (30, 'fc', 'Sete Lagoas', 'MG'), (40, 'Loja Velha', 'BH', 'MG')",
        "INSERT INTO Setores (Id, FilialId, Nome, MinimoFuncionarios) VALUES (1, 10, 'Caixa', 2), (2, 20, 'Estoque', 1), (3, 30, 'Vendas', 1)",
        "INSERT INTO Funcionarios (Id, FilialId, SetorId, Nome, Ativo) VALUES (1, 10, 1, 'Ana', 1), (2, 20, 2, 'Bia', 1), (3, 30, 3, 'Caio', 0)",
        "INSERT INTO Escalas (FuncionarioId, Data, Removido) VALUES (1, '2026-11-02', 0), (2, '2026-11-03', 1)",
        "INSERT INTO EscalasPadrao (FuncionarioId, DiaSemana) VALUES (1, 1), (1, 2), (2, 3)",
        "INSERT INTO DiasFilial (FilialId, Data, Fechado) VALUES (10, '2026-11-15', 1), (20, '2026-11-15', 0)",
        "INSERT INTO Feriados (Data, Nome, Abrangencia, UF, Cidade, FilialId) VALUES ('2026-01-01', 'Ano Novo', 1, NULL, NULL, NULL), ('2026-08-15', 'Aniversário', 3, 'MG', 'Sete Lagoas', 30)",
    ];

    [Fact]
    public async Task Converte_vinculos_pelo_nome_sem_perder_escalas_e_preserva_filiais_antigas()
    {
        using var c = new Cenario();
        await c.CriarBancoAntigoAsync(SementeCompleta);

        await AssertProntoAsync(c);
        Assert.Single(Directory.GetFiles(c.Caminhos.DiretorioBackups, "pre-migracao-*.db"));

        await using (var db = c.Abrir())
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal([(1, Filial.Matriz), (2, Filial.Deposito), (3, Filial.FC)],
                (await db.Setores.OrderBy(s => s.Id).ToListAsync()).Select(s => (s.Id, s.Filial)));
            Assert.Equal([(1, Filial.Matriz, 1), (2, Filial.Deposito, 2), (3, Filial.FC, 3)],
                (await db.Funcionarios.OrderBy(f => f.Id).ToListAsync()).Select(f => (f.Id, f.Filial, f.SetorId)));
            Assert.False((await db.Funcionarios.FindAsync(3))!.Ativo);
            Assert.Equal(2, await db.Escalas.CountAsync());
            Assert.Equal(3, await db.EscalasPadrao.CountAsync());
            Assert.Equal([(Filial.Matriz, true), (Filial.Deposito, false)],
                (await db.DiasFilial.OrderBy(d => d.Filial).ToListAsync()).Select(d => (d.Filial, d.Fechado)));
            var feriados = await db.Feriados.OrderBy(f => f.Data).ToListAsync();
            Assert.Equal([null, Filial.FC], feriados.Select(f => f.Filial));
        }

        Assert.Equal(0, await c.ContarAsync("SELECT count(*) FROM sqlite_master WHERE name = 'Filiais'"));
        var antigas = await c.ConsultarAsync("SELECT Id, Nome, Cidade, FilialFixa FROM FiliaisAntigas ORDER BY Id");
        Assert.Equal(4, antigas.Count);
        Assert.Equal([1L, 5L, 3L, null], antigas.Select(a => a[3]));
        Assert.Equal("Loja Velha", antigas[3][1]);
        Assert.Equal("Sete Lagoas", antigas[0][2]);
        Assert.Equal(1, await c.ContarAsync("PRAGMA foreign_keys"));
        Assert.Empty(await c.ConsultarAsync("PRAGMA foreign_key_check"));
    }

    [Fact]
    public async Task Filial_com_dados_sem_correspondencia_bloqueia_com_aviso_e_nao_altera_o_banco()
    {
        using var c = new Cenario();
        await c.CriarBancoAntigoAsync(
            "INSERT INTO Filiais (Id, Nome, Cidade, UF) VALUES (1, 'Matriz', 'X', 'MG'), (2, 'Centro', 'X', 'MG')",
            "INSERT INTO Setores (Id, FilialId, Nome, MinimoFuncionarios) VALUES (1, 1, 'Caixa', 1), (2, 2, 'Caixa', 1)");

        var r = await c.InicializarAsync();

        Assert.True(r.Situacao == SituacaoBanco.Erro, r.Mensagem);
        Assert.Contains("\"Centro\"", r.Mensagem);
        Assert.DoesNotContain("\"Matriz\"", r.Mensagem);
        Assert.Contains("não foi alterado", r.Mensagem);
        await using var db = c.Abrir();
        Assert.Contains(FiliaisFixas.IdMigracao, await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, await c.ContarAsync("SELECT count(*) FROM Setores WHERE FilialId IN (1, 2)"));
    }

    [Fact]
    public async Task Duas_filiais_com_dados_no_mesmo_nome_fixo_bloqueiam()
    {
        using var c = new Cenario();
        await c.CriarBancoAntigoAsync(
            "INSERT INTO Filiais (Id, Nome, Cidade, UF) VALUES (1, 'Matriz', 'X', 'MG'), (2, 'MATRIZ', 'Y', 'MG')",
            "INSERT INTO DiasFilial (FilialId, Data, Fechado) VALUES (1, '2026-11-15', 1), (2, '2026-11-15', 0)");

        var r = await c.InicializarAsync();

        Assert.True(r.Situacao == SituacaoBanco.Erro, r.Mensagem);
        Assert.Contains("mesma filial (Matriz)", r.Mensagem);
        Assert.Equal(2, await c.ContarAsync("SELECT count(*) FROM DiasFilial"));
    }

    [Fact]
    public async Task Empacotar_recusa_banco_que_o_cliente_nao_conseguiria_atualizar()
    {
        using var c = new Cenario();
        await c.CriarBancoAntigoAsync(
            "INSERT INTO Filiais (Id, Nome, Cidade, UF) VALUES (1, 'Centro', 'X', 'MG')",
            "INSERT INTO Setores (Id, FilialId, Nome, MinimoFuncionarios) VALUES (1, 1, 'Caixa', 1)");
        new ChaveStore(c.Caminhos).Salvar(c.Chave);

        var ex = await Assert.ThrowsAsync<EmpacotamentoException>(() =>
            new EmpacotadorInstalacao(c.Caminhos, new ChaveStore(c.Caminhos)).EmpacotarAsync(c.Amb.Pacote));
        Assert.Contains("\"Centro\"", ex.Message);
        Assert.False(File.Exists(Path.Combine(c.Amb.Pacote, "escalas.db")));
    }

    [Fact]
    public async Task Filial_sem_correspondencia_e_sem_dados_so_fica_guardada()
    {
        using var c = new Cenario();
        await c.CriarBancoAntigoAsync("INSERT INTO Filiais (Id, Nome, Cidade, UF) VALUES (7, 'Filial Teste', 'X', 'MG')");

        await AssertProntoAsync(c);
        var antigas = await c.ConsultarAsync("SELECT Nome, FilialFixa FROM FiliaisAntigas");
        Assert.Equal("Filial Teste", Assert.Single(antigas)[0]);
        Assert.Null(antigas[0][1]);
    }

    [Fact]
    public async Task Trava_da_propria_migration_desfaz_tudo_se_aplicada_sem_a_verificacao()
    {
        using var c = new Cenario();
        await c.CriarBancoAntigoAsync([.. SementeCompleta,
            "INSERT INTO Setores (Id, FilialId, Nome, MinimoFuncionarios) VALUES (9, 40, 'Antigo', 0)"]);

        await using (var db = c.Abrir())
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.MigrateAsync());
        SqliteConnection.ClearAllPools();

        Assert.Equal(1, await c.ContarAsync("SELECT count(*) FROM sqlite_master WHERE name = 'Filiais'"));
        Assert.Equal(0, await c.ContarAsync("SELECT count(*) FROM sqlite_master WHERE name LIKE 'ef_temp_%' OR name = 'FiliaisAntigas'"));
        Assert.Equal(4, await c.ContarAsync("SELECT count(*) FROM Setores WHERE FilialId IS NOT NULL"));
        Assert.Equal(2, await c.ContarAsync("SELECT count(*) FROM Escalas"));
        Assert.Equal(3, await c.ContarAsync("SELECT count(*) FROM EscalasPadrao"));
    }

    [Fact]
    public async Task Down_recria_as_filiais_e_os_vinculos()
    {
        using var c = new Cenario();
        await c.CriarBancoAntigoAsync(SementeCompleta);
        await AssertProntoAsync(c);
        await using (var db = c.Abrir())
        {
            db.Setores.Add(new Setor { Filial = Filial.Interiores, Nome = "Novo" });
            await db.SaveChangesAsync();
            await db.GetService<IMigrator>().MigrateAsync(AntesDasFiliaisFixas);
        }
        SqliteConnection.ClearAllPools();

        var setores = await c.ConsultarAsync("SELECT s.Nome, f.Nome FROM Setores s JOIN Filiais f ON f.Id = s.FilialId ORDER BY s.Id");
        Assert.Equal(["  MATRIZ ", "Depósito", "fc", "Interiores"], setores.Select(s => s[1]));
        Assert.Equal(2, await c.ContarAsync("SELECT count(*) FROM Escalas"));
        Assert.Equal(0, await c.ContarAsync("SELECT count(*) FROM sqlite_master WHERE name = 'FiliaisAntigas'"));
    }
}
