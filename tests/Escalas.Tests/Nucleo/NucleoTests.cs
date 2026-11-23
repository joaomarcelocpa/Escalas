using Escalas.Application.Nucleo;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Modulos;
using Escalas.Infrastructure.Nucleo;
using Escalas.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Tests.Nucleo;

public sealed class AmbienteTemp : IDisposable
{
    public string Pasta { get; } = Path.Combine(Path.GetTempPath(), "escalas-tests-" + Guid.NewGuid().ToString("N"));
    public string Entrega => Path.Combine(Pasta, "entrega");
    public string Dados => Path.Combine(Pasta, "dados");
    public string Pacote => Path.Combine(Pasta, "pacote");

    public AmbienteTemp() => Directory.CreateDirectory(Pasta);

    public CaminhosApp CaminhosCliente() => new(Path.Combine(Dados, "escalas.db"), Dados, Pacote);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Pasta, true); } catch (IOException) { }
    }
}

public class NucleoTests
{
    private static async Task<ResultadoCriacao> CriarEmpresa(AmbienteTemp amb) =>
        await new CriadorDeEmpresa().CriarAsync("Padaria Exemplo", "00.000.000/0001-00", amb.Entrega);

    /// <summary>Simula a entrega ao cliente: copia .db e .chave para a pasta de dados.</summary>
    private static void Entregar(AmbienteTemp amb, ResultadoCriacao r)
    {
        Directory.CreateDirectory(amb.Dados);
        File.Copy(r.CaminhoBanco, Path.Combine(amb.Dados, "escalas.db"));
        File.Copy(r.CaminhoChave, Path.Combine(amb.Dados, "escalas.chave"));
    }

    private static ServiceProvider Provedor(CaminhosApp caminhos) =>
        new ServiceCollection().AdicionarNucleo(caminhos).BuildServiceProvider();

    [Fact]
    public async Task Admin_cria_banco_e_app_abre_apos_entrega()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        Entregar(amb, r);

        var caminhos = amb.CaminhosCliente();
        await using var sp = Provedor(caminhos);
        var init = await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync();
        Assert.Equal(SituacaoBanco.Pronto, init.Situacao);

        var factory = sp.GetRequiredService<IDbContextFactory<EscalasDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var empresa = Assert.Single(await db.Empresas.ToListAsync());
        Assert.Equal("Padaria Exemplo", empresa.Nome);
    }

    [Fact]
    public async Task Chave_e_importada_no_primeiro_uso_e_arquivo_apagado()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        Entregar(amb, r);
        var caminhos = amb.CaminhosCliente();
        Assert.True(File.Exists(caminhos.CaminhoChaveEntrega));

        await using var sp = Provedor(caminhos);
        await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync();

        Assert.False(File.Exists(caminhos.CaminhoChaveEntrega));
        var chave = sp.GetRequiredService<IChaveStore>().Obter();
        Assert.False(string.IsNullOrEmpty(chave));

        if (!OperatingSystem.IsWindows())
        {
            var arquivo = Path.Combine(amb.Dados, "chave.key");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(arquivo));
        }

        // Segunda abertura funciona sem o .chave.
        await using var sp2 = Provedor(caminhos);
        Assert.Equal(SituacaoBanco.Pronto, (await sp2.GetRequiredService<IInicializadorBanco>().InicializarAsync()).Situacao);
    }

    [Fact]
    public async Task Banco_e_realmente_criptografado()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        var chave = (await File.ReadAllTextAsync(r.CaminhoChave)).Trim();

        var cabecalho = (await File.ReadAllBytesAsync(r.CaminhoBanco)).Take(15).ToArray();
        Assert.NotEqual("SQLite format 3", System.Text.Encoding.ASCII.GetString(cabecalho));

        BancoFabrica.Iniciar();
        // Sem chave ou com chave errada: o SQLCipher recusa o arquivo (na abertura ou na primeira consulta).
        static void TentarLer(string connectionString)
        {
            using var c = new SqliteConnection(connectionString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master";
            cmd.ExecuteScalar();
        }
        Assert.Throws<SqliteException>(() => TentarLer($"Data Source={r.CaminhoBanco};Pooling=False"));
        Assert.Throws<SqliteException>(() => TentarLer(BancoFabrica.ConnectionString(r.CaminhoBanco, BancoFabrica.GerarChave())));
        // Chave correta: abre.
        using (var c = new SqliteConnection(BancoFabrica.ConnectionString(r.CaminhoBanco, chave)))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master";
            Assert.True((long)cmd.ExecuteScalar()! > 0);
        }
    }

    [Fact]
    public async Task Sem_banco_o_inicializador_informa_banco_ausente()
    {
        using var amb = new AmbienteTemp();
        await using var sp = Provedor(amb.CaminhosCliente());
        var r = await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync();
        Assert.Equal(SituacaoBanco.BancoAusente, r.Situacao);
        Assert.False(string.IsNullOrWhiteSpace(r.Mensagem));
    }

    [Fact]
    public async Task Banco_sem_chave_informa_chave_ausente()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        Directory.CreateDirectory(amb.Dados);
        File.Copy(r.CaminhoBanco, Path.Combine(amb.Dados, "escalas.db"));

        await using var sp = Provedor(amb.CaminhosCliente());
        Assert.Equal(SituacaoBanco.ChaveAusente, (await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync()).Situacao);
    }

    [Fact]
    public async Task Admin_nao_sobrescreve_banco_existente_e_valida_entrada()
    {
        using var amb = new AmbienteTemp();
        await CriarEmpresa(amb);
        await Assert.ThrowsAsync<CriacaoEmpresaException>(() => CriarEmpresa(amb));
        await Assert.ThrowsAsync<CriacaoEmpresaException>(() =>
            new CriadorDeEmpresa().CriarAsync("X", "  ", Path.Combine(amb.Pasta, "outra")));
    }

    [Fact]
    public async Task Empresa_aceita_no_maximo_uma_linha_por_constraint()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        var chave = (await File.ReadAllTextAsync(r.CaminhoChave)).Trim();

        await using var db = new EscalasDbContext(BancoFabrica.Opcoes(r.CaminhoBanco, chave));
        db.Empresas.Add(new Empresa { Id = 2, Nome = "Outra", Documento = "2" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Backup_automatico_mantem_apenas_as_ultimas_N()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        Entregar(amb, r);
        var caminhos = amb.CaminhosCliente();
        var backup = new BackupService(caminhos, new BackupOpcoes { Manter = 3 });

        var criados = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            criados.Add(backup.BackupAutomatico()!);
            Thread.Sleep(10);
        }

        var restantes = Directory.GetFiles(caminhos.DiretorioBackups, "auto-*.db").OrderBy(f => f).ToList();
        Assert.Equal(3, restantes.Count);
        Assert.Equal(criados.Skip(2).OrderBy(f => f), restantes);
    }

    [Fact]
    public async Task Exportar_backup_copia_o_banco()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        Entregar(amb, r);
        var backup = new BackupService(amb.CaminhosCliente(), new BackupOpcoes());
        var destino = Path.Combine(amb.Pasta, "export", "copia.db");

        backup.Exportar(destino);

        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(amb.Dados, "escalas.db")), await File.ReadAllBytesAsync(destino));
    }

    [Fact]
    public void Backup_sem_banco_retorna_null_e_exportar_falha()
    {
        using var amb = new AmbienteTemp();
        var backup = new BackupService(amb.CaminhosCliente(), new BackupOpcoes());
        Assert.Null(backup.BackupAutomatico());
        Assert.Throws<FileNotFoundException>(() => backup.Exportar(Path.Combine(amb.Pasta, "x.db")));
    }

    [Fact]
    public async Task Migracao_pendente_faz_backup_antes_e_aplica()
    {
        using var amb = new AmbienteTemp();
        Directory.CreateDirectory(amb.Dados);
        var caminhos = amb.CaminhosCliente();
        var chave = BancoFabrica.GerarChave();

        // Banco cifrado "antigo": existe, mas sem nenhuma migration aplicada.
        using (var c = new SqliteConnection(BancoFabrica.ConnectionString(caminhos.CaminhoBanco, chave)))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE Legado (Id INTEGER PRIMARY KEY); INSERT INTO Legado VALUES (42);";
            cmd.ExecuteNonQuery();
        }
        await using var sp = Provedor(caminhos);
        sp.GetRequiredService<IChaveStore>().Salvar(chave);

        var r = await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync();
        Assert.Equal(SituacaoBanco.Pronto, r.Situacao);

        // Backup prévio existe e ainda NÃO tem o esquema novo, mas tem os dados antigos.
        var pre = Assert.Single(Directory.GetFiles(caminhos.DiretorioBackups, "pre-migracao-*.db"));
        using (var c = new SqliteConnection(BancoFabrica.ConnectionString(pre, chave)))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'Empresa'";
            Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
            cmd.CommandText = "SELECT Id FROM Legado";
            Assert.Equal(42L, (long)cmd.ExecuteScalar()!);
        }

        // Banco real agora tem o esquema.
        var factory = sp.GetRequiredService<IDbContextFactory<EscalasDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Banco_antigo_com_usuarios_migra_e_remove_tabela_de_login()
    {
        using var amb = new AmbienteTemp();
        Directory.CreateDirectory(amb.Dados);
        var caminhos = amb.CaminhosCliente();
        var chave = BancoFabrica.GerarChave();

        // Banco no esquema anterior à remoção do login, com um usuário cadastrado.
        await using (var antigo = new EscalasDbContext(BancoFabrica.Opcoes(caminhos.CaminhoBanco, chave)))
        {
            await antigo.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261016142416_EscalaPadrao");
            await antigo.Database.ExecuteSqlRawAsync("INSERT INTO Usuarios (Login, SenhaHash, Papel) VALUES ('admin', 'hash', 1)");
        }
        SqliteConnection.ClearAllPools();

        await using var sp = Provedor(caminhos);
        sp.GetRequiredService<IChaveStore>().Salvar(chave);
        Assert.Equal(SituacaoBanco.Pronto, (await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync()).Situacao);

        Assert.Single(Directory.GetFiles(caminhos.DiretorioBackups, "pre-migracao-*.db"));
        var factory = sp.GetRequiredService<IDbContextFactory<EscalasDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var conexao = db.Database.GetDbConnection();
        await conexao.OpenAsync();
        await using var cmd = conexao.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'Usuarios'";
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Pacote_ao_lado_do_exe_e_importado_no_primeiro_uso_e_chave_apagada()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        // Máquina de origem: banco já em uso, com a chave guardada.
        var origem = new CaminhosApp(r.CaminhoBanco, Path.Combine(amb.Pasta, "origem"), Path.Combine(amb.Pasta, "nada"));
        new ChaveStore(origem).Salvar((await File.ReadAllTextAsync(r.CaminhoChave)).Trim());
        File.Delete(r.CaminhoChave);

        await new EmpacotadorInstalacao(origem, new ChaveStore(origem)).EmpacotarAsync(amb.Pacote);
        Assert.True(File.Exists(Path.Combine(amb.Pacote, "escalas.db")));
        Assert.True(File.Exists(Path.Combine(amb.Pacote, "escalas.chave")));

        // PC da empresa: sem banco na pasta de dados, só o pacote ao lado do executável.
        var caminhos = amb.CaminhosCliente();
        await using var sp = Provedor(caminhos);
        Assert.Equal(SituacaoBanco.Pronto, (await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync()).Situacao);

        Assert.True(File.Exists(caminhos.CaminhoBanco));
        Assert.False(File.Exists(Path.Combine(amb.Pacote, "escalas.chave")));
        var factory = sp.GetRequiredService<IDbContextFactory<EscalasDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("Padaria Exemplo", Assert.Single(await db.Empresas.ToListAsync()).Nome);
    }

    [Fact]
    public async Task Pacote_nao_sobrescreve_banco_ja_instalado()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        Entregar(amb, r);
        Directory.CreateDirectory(amb.Pacote);
        await File.WriteAllTextAsync(Path.Combine(amb.Pacote, "escalas.db"), "outro banco");
        var original = await File.ReadAllBytesAsync(Path.Combine(amb.Dados, "escalas.db"));

        await using var sp = Provedor(amb.CaminhosCliente());
        Assert.Equal(SituacaoBanco.Pronto, (await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync()).Situacao);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(amb.Dados, "escalas.db")));
    }

    [Fact]
    public async Task Empacotar_sem_chave_falha_com_mensagem()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        File.Delete(r.CaminhoChave);
        var origem = new CaminhosApp(r.CaminhoBanco, Path.Combine(amb.Pasta, "origem"), Path.Combine(amb.Pasta, "nada"));
        await Assert.ThrowsAsync<EmpacotamentoException>(() =>
            new EmpacotadorInstalacao(origem, new ChaveStore(origem)).EmpacotarAsync(amb.Pacote));
    }

    [Fact]
    public async Task Banco_em_dia_nao_gera_backup_pre_migracao()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        Entregar(amb, r);
        var caminhos = amb.CaminhosCliente();
        await using var sp = Provedor(caminhos);

        await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync();

        Assert.False(Directory.Exists(caminhos.DiretorioBackups) && Directory.GetFiles(caminhos.DiretorioBackups).Length > 0);
    }

    [Fact]
    public void Caminho_do_banco_respeita_variavel_de_ambiente()
    {
        var anterior = Environment.GetEnvironmentVariable(CaminhosApp.VariavelBanco);
        try
        {
            var alvo = Path.Combine(Path.GetTempPath(), "x", "meu.db");
            Environment.SetEnvironmentVariable(CaminhosApp.VariavelBanco, alvo);
            Assert.Equal(alvo, new CaminhosApp().CaminhoBanco);
            Environment.SetEnvironmentVariable(CaminhosApp.VariavelBanco, null);
            Assert.EndsWith(Path.Combine("Escalas", "escalas.db"), new CaminhosApp().CaminhoBanco);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaminhosApp.VariavelBanco, anterior);
        }
    }
}
