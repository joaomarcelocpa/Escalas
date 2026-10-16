using System.Security.Cryptography;
using System.Text;
using Escalas.Application.Nucleo;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Nucleo;

public class BcryptPasswordHasher : IPasswordHasher
{
    private const int Custo = 12;

    public string Hash(string senha) => BCrypt.Net.BCrypt.HashPassword(senha, Custo);

    public bool Verificar(string senha, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(senha, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }
}

/// <summary>Locais de dados da instalação. ESCALAS_DB sobrescreve o caminho do banco.</summary>
public class CaminhosApp
{
    public const string VariavelBanco = "ESCALAS_DB";

    public string DiretorioDados { get; }
    public string CaminhoBanco { get; }
    public string DiretorioBackups { get; }

    /// <summary>Arquivo de chave entregue pelo Admin, ao lado do .db.</summary>
    public string CaminhoChaveEntrega => Path.ChangeExtension(CaminhoBanco, ".chave");

    public CaminhosApp(string? caminhoBanco = null, string? diretorioDados = null)
    {
        var env = Environment.GetEnvironmentVariable(VariavelBanco);
        DiretorioDados = diretorioDados
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Escalas");
        CaminhoBanco = caminhoBanco
            ?? (string.IsNullOrWhiteSpace(env) ? Path.Combine(DiretorioDados, "escalas.db") : Path.GetFullPath(env));
        DiretorioBackups = Path.Combine(Path.GetDirectoryName(CaminhoBanco)!, "backups");
    }
}

public static class BancoFabrica
{
    private static readonly object Trava = new();
    private static bool _iniciado;

    /// <summary>Inicializa o provedor nativo (SQLCipher). Seguro para chamar várias vezes.</summary>
    public static void Iniciar()
    {
        lock (Trava)
        {
            if (_iniciado) return;
            SQLitePCL.Batteries_V2.Init();
            _iniciado = true;
        }
    }

    public static string ConnectionString(string caminho, string chave)
    {
        Iniciar();
        return new SqliteConnectionStringBuilder
        {
            DataSource = caminho,
            Password = chave,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
    }

    public static DbContextOptions<EscalasDbContext> Opcoes(string caminho, string chave) =>
        new DbContextOptionsBuilder<EscalasDbContext>().UseSqlite(ConnectionString(caminho, chave)).Options;

    public static string GerarChave() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}

/// <summary>
/// Windows: DPAPI (escopo do usuário). Linux/macOS: arquivo no diretório de dados com permissão 600.
/// </summary>
public class ChaveStore(CaminhosApp caminhos) : IChaveStore
{
    private string Arquivo => Path.Combine(caminhos.DiretorioDados, OperatingSystem.IsWindows() ? "chave.dpapi" : "chave.key");

    public string? Obter()
    {
        if (!File.Exists(Arquivo)) return null;
        var bytes = File.ReadAllBytes(Arquivo);
        if (OperatingSystem.IsWindows())
            bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Salvar(string chave)
    {
        Directory.CreateDirectory(caminhos.DiretorioDados);
        var bytes = Encoding.UTF8.GetBytes(chave);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(Arquivo, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
            return;
        }
        var opcoes = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        using var fs = new FileStream(Arquivo, opcoes);
        fs.Write(bytes);
    }
}

public class BackupOpcoes
{
    public int Manter { get; set; } = 7;
}

public class BackupService(CaminhosApp caminhos, BackupOpcoes opcoes) : IBackupService
{
    private const string PrefixoAuto = "auto-";
    private const string PrefixoPreMigracao = "pre-migracao-";

    public void Exportar(string destino)
    {
        if (!File.Exists(caminhos.CaminhoBanco))
            throw new FileNotFoundException("Banco de dados não encontrado.", caminhos.CaminhoBanco);
        Copiar(destino);
    }

    public string? BackupAutomatico()
    {
        var destino = Criar(PrefixoAuto);
        if (destino is null) return null;
        var antigos = Directory.GetFiles(caminhos.DiretorioBackups, PrefixoAuto + "*.db")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(Math.Max(1, opcoes.Manter));
        foreach (var f in antigos) File.Delete(f);
        return destino;
    }

    public string? BackupPreMigracao() => Criar(PrefixoPreMigracao);

    private string? Criar(string prefixo)
    {
        if (!File.Exists(caminhos.CaminhoBanco)) return null;
        Directory.CreateDirectory(caminhos.DiretorioBackups);
        var destino = Path.Combine(caminhos.DiretorioBackups, $"{prefixo}{DateTime.Now:yyyyMMdd-HHmmss-fff}.db");
        Copiar(destino);
        return destino;
    }

    private void Copiar(string destino)
    {
        SqliteConnection.ClearAllPools();
        var pasta = Path.GetDirectoryName(Path.GetFullPath(destino));
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
        File.Copy(caminhos.CaminhoBanco, destino, overwrite: true);
    }
}

public class AutenticacaoService(IDbContextFactory<EscalasDbContext> factory, IPasswordHasher hasher) : IAutenticacaoService
{
    // Hash válido, verificado quando o usuário não existe (evita diferença de tempo).
    private static readonly string HashFalso = BCrypt.Net.BCrypt.HashPassword("x", 12);

    public async Task<Usuario?> EntrarAsync(string login, string senha, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(senha)) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var loginLimpo = login.Trim();
        var usuario = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Login == loginLimpo, ct);
        if (usuario is null)
        {
            hasher.Verificar(senha, HashFalso);
            return null;
        }
        return hasher.Verificar(senha, usuario.SenhaHash) ? usuario : null;
    }
}

public class InicializadorBanco(CaminhosApp caminhos, IChaveStore chaves, IBackupService backup) : IInicializadorBanco
{
    public async Task<ResultadoInicializacao> InicializarAsync(CancellationToken ct = default)
    {
        if (!File.Exists(caminhos.CaminhoBanco))
            return new(SituacaoBanco.BancoAusente,
                $"Nenhum banco de dados foi encontrado em:\n{caminhos.CaminhoBanco}\n\nCopie o arquivo escalas.db fornecido pelo seu fornecedor para essa pasta e abra o aplicativo novamente.");

        try
        {
            ImportarChaveSeNecessario();
            var chave = chaves.Obter();
            if (chave is null)
                return new(SituacaoBanco.ChaveAusente,
                    $"A chave do banco não foi encontrada. Copie o arquivo {Path.GetFileName(caminhos.CaminhoChaveEntrega)} fornecido junto com o banco para a mesma pasta do banco e abra o aplicativo novamente.");

            await using var db = new EscalasDbContext(BancoFabrica.Opcoes(caminhos.CaminhoBanco, chave));
            // Lê o esquema: falha com SqliteException se a chave não corresponder ao arquivo.
            var aplicadas = await db.Database.GetAppliedMigrationsAsync(ct);
            _ = aplicadas.Count();
            if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            {
                backup.BackupPreMigracao();
                await db.Database.MigrateAsync(ct);
            }
            return new(SituacaoBanco.Pronto);
        }
        catch (SqliteException ex)
        {
            return new(SituacaoBanco.Erro, $"Não foi possível abrir o banco de dados (chave incorreta ou arquivo corrompido). {ex.Message}");
        }
        catch (Exception ex)
        {
            return new(SituacaoBanco.Erro, $"Erro ao preparar o banco de dados: {ex.Message}");
        }
    }

    private void ImportarChaveSeNecessario()
    {
        if (chaves.Obter() is not null || !File.Exists(caminhos.CaminhoChaveEntrega)) return;
        var chave = File.ReadAllText(caminhos.CaminhoChaveEntrega).Trim();
        if (chave.Length == 0) return;
        chaves.Salvar(chave);
        if (chaves.Obter() == chave) File.Delete(caminhos.CaminhoChaveEntrega);
    }
}

public record ResultadoCriacao(string CaminhoBanco, string CaminhoChave);

/// <summary>Gerador de banco: cria um .db cifrado, com migrations, uma única Empresa e o admin inicial.</summary>
public class CriadorDeEmpresa(IPasswordHasher hasher)
{
    public async Task<ResultadoCriacao> CriarAsync(string nome, string documento, string adminLogin, string adminSenha, string saida, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new CriacaoEmpresaException("Informe o nome da empresa.");
        if (string.IsNullOrWhiteSpace(documento)) throw new CriacaoEmpresaException("Informe o documento da empresa.");
        if (string.IsNullOrWhiteSpace(adminLogin)) throw new CriacaoEmpresaException("Informe o login do administrador.");
        if (adminSenha is null || adminSenha.Length < 6) throw new CriacaoEmpresaException("A senha do administrador deve ter ao menos 6 caracteres.");

        // saída terminando em .db = arquivo; caso contrário, diretório que receberá escalas.db e escalas.chave
        var caminhoBanco = saida.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(saida)
            : Path.Combine(Path.GetFullPath(saida), "escalas.db");
        var caminhoChave = Path.ChangeExtension(caminhoBanco, ".chave");
        if (File.Exists(caminhoBanco)) throw new CriacaoEmpresaException($"Já existe um banco em {caminhoBanco}. Escolha outra saída.");
        Directory.CreateDirectory(Path.GetDirectoryName(caminhoBanco)!);

        var chave = BancoFabrica.GerarChave();
        try
        {
            await using (var db = new EscalasDbContext(BancoFabrica.Opcoes(caminhoBanco, chave)))
            {
                await db.Database.MigrateAsync(ct);
                if (await db.Empresas.AnyAsync(ct))
                    throw new CriacaoEmpresaException("O banco já possui uma empresa; só é permitida uma por banco.");
                db.Empresas.Add(new Empresa { Id = 1, Nome = nome.Trim(), Documento = documento.Trim() });
                db.Usuarios.Add(new Usuario { Login = adminLogin.Trim(), SenhaHash = hasher.Hash(adminSenha), Papel = PapelUsuario.Administrador });
                await db.SaveChangesAsync(ct);
            }
            await File.WriteAllTextAsync(caminhoChave, chave, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(caminhoChave, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(caminhoBanco)) File.Delete(caminhoBanco);
            if (File.Exists(caminhoChave)) File.Delete(caminhoChave);
            throw;
        }
        SqliteConnection.ClearAllPools();
        return new(caminhoBanco, caminhoChave);
    }
}
