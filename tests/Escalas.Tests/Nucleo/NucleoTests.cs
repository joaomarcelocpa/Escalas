using Escalas.Application.Nucleo;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Modulos;
using Escalas.Infrastructure.Nucleo;
using Escalas.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Tests.Nucleo;

public sealed class AmbienteTemp : IDisposable
{
    public string Pasta { get; } = Path.Combine(Path.GetTempPath(), "escalas-tests-" + Guid.NewGuid().ToString("N"));
    public string Entrega => Path.Combine(Pasta, "entrega");
    public string Dados => Path.Combine(Pasta, "dados");

    public AmbienteTemp() => Directory.CreateDirectory(Pasta);

    public CaminhosApp CaminhosCliente() => new(Path.Combine(Dados, "escalas.db"), Dados);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Pasta, true); } catch (IOException) { }
    }
}

public class NucleoTests
{
    private static readonly BcryptPasswordHasher Hasher = new();

    private static async Task<ResultadoCriacao> CriarEmpresa(AmbienteTemp amb, string senha = "segredo123") =>
        await new CriadorDeEmpresa(Hasher).CriarAsync("Padaria Exemplo", "00.000.000/0001-00", "admin", senha, amb.Entrega);

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
    public void Hash_verifica_senha_correta_e_rejeita_incorreta()
    {
        var hash = Hasher.Hash("abc12345");
        Assert.NotEqual("abc12345", hash);
        Assert.True(Hasher.Verificar("abc12345", hash));
        Assert.False(Hasher.Verificar("errada", hash));
        Assert.False(Hasher.Verificar("abc12345", "não-é-hash"));
    }

    [Fact]
    public async Task Admin_cria_banco_e_login_funciona_apos_entrega()
    {
        using var amb = new AmbienteTemp();
        var r = await CriarEmpresa(amb);
        Entregar(amb, r);

        var caminhos = amb.CaminhosCliente();
        await using var sp = Provedor(caminhos);
        var init = await sp.GetRequiredService<IInicializadorBanco>().InicializarAsync();
        Assert.Equal(SituacaoBanco.Pronto, init.Situacao);

        var auth = sp.GetRequiredService<IAutenticacaoService>();
        var usuario = await auth.EntrarAsync("admin", "segredo123");
        Assert.NotNull(usuario);
        Assert.Equal(PapelUsuario.Administrador, usuario.Papel);
        Assert.NotEqual("segredo123", usuario.SenhaHash);
        Assert.Null(await auth.EntrarAsync("admin", "errada"));
        Assert.Null(await auth.EntrarAsync("inexistente", "segredo123"));

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
            new CriadorDeEmpresa(Hasher).CriarAsync("X", "1", "admin", "123", Path.Combine(amb.Pasta, "outra")));
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
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'Usuario'";
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
