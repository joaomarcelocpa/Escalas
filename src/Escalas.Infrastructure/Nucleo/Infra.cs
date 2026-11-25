using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Escalas.Application.Nucleo;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Nucleo;

/// <summary>Locais de dados da instalação. ESCALAS_DB sobrescreve o caminho do banco.</summary>
public class CaminhosApp
{
    public const string VariavelBanco = "ESCALAS_DB";

    public string DiretorioDados { get; }
    public string CaminhoBanco { get; }
    public string DiretorioBackups { get; }

    /// <summary>Pasta do executável. Um escalas.db/escalas.chave aqui é importado no primeiro uso (pacote de instalação).</summary>
    public string DiretorioPacote { get; }

    public const string NomeBancoPacote = "escalas.db";
    public const string NomeChavePacote = "escalas.chave";

    /// <summary>Arquivo de chave entregue pelo Admin, ao lado do .db.</summary>
    public string CaminhoChaveEntrega => Path.ChangeExtension(CaminhoBanco, ".chave");

    public CaminhosApp(string? caminhoBanco = null, string? diretorioDados = null, string? diretorioPacote = null)
    {
        var env = Environment.GetEnvironmentVariable(VariavelBanco);
        DiretorioDados = diretorioDados
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Escalas");
        CaminhoBanco = caminhoBanco
            ?? (string.IsNullOrWhiteSpace(env) ? Path.Combine(DiretorioDados, "escalas.db") : Path.GetFullPath(env));
        DiretorioBackups = Path.Combine(Path.GetDirectoryName(CaminhoBanco)!, "backups");
        DiretorioPacote = diretorioPacote ?? AppContext.BaseDirectory;
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

/// <summary>Banco e chave que vieram junto com o aplicativo (dentro do .exe).</summary>
public interface IPacoteInstalacao
{
    /// <summary>Conteúdo do banco embutido, ou null se este executável não traz banco.</summary>
    Stream? AbrirBanco();
    string? LerChave();
}

/// <summary>
/// Lê escalas.db e escalas.chave embutidos como recurso no executável (gerado pelo empacotar.sh).
/// Num build de desenvolvimento não há recursos e nada é importado.
/// </summary>
public class PacoteEmbutido(Assembly? assembly) : IPacoteInstalacao
{
    public const string RecursoBanco = "Escalas.Pacote.escalas.db";
    public const string RecursoChave = "Escalas.Pacote.escalas.chave";

    public Stream? AbrirBanco() => assembly?.GetManifestResourceStream(RecursoBanco);

    public string? LerChave()
    {
        using var s = assembly?.GetManifestResourceStream(RecursoChave);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd().Trim() is { Length: > 0 } chave ? chave : null;
    }
}

public class InicializadorBanco(CaminhosApp caminhos, IChaveStore chaves, IBackupService backup, IPacoteInstalacao pacote) : IInicializadorBanco
{
    public async Task<ResultadoInicializacao> InicializarAsync(CancellationToken ct = default)
    {
        try { ImportarPacoteSeNecessario(); }
        catch (Exception ex)
        {
            return new(SituacaoBanco.Erro, $"Não foi possível instalar o banco que veio junto com o aplicativo: {ex.Message}");
        }
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
            var pendentes = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pendentes.Count > 0)
            {
                if (await VerificacaoFiliaisFixas.VerificarAsync(db, pendentes, ct) is { } aviso)
                    return new(SituacaoBanco.Erro, aviso);
                backup.BackupPreMigracao();
                await db.Database.MigrateAsync(ct);
            }
            ApagarChaveDoPacote();
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

    /// <summary>
    /// Primeiro uso no PC da empresa: se ainda não há banco na pasta de dados e o pacote trouxe
    /// escalas.db (e escalas.chave) ao lado do executável, copia o banco e a chave para a pasta de dados.
    /// Nunca sobrescreve um banco existente. A chave é guardada protegida pelo SO e a cópia do pacote é apagada.
    /// </summary>
    private void ImportarPacoteSeNecessario()
    {
        if (File.Exists(caminhos.CaminhoBanco)) return;
        if (ImportarPacoteEmbutido()) return;
        var bancoPacote = Path.Combine(caminhos.DiretorioPacote, CaminhosApp.NomeBancoPacote);
        if (!File.Exists(bancoPacote)) return;
        if (Path.GetFullPath(bancoPacote) == Path.GetFullPath(caminhos.CaminhoBanco)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(caminhos.CaminhoBanco)!);
        var chavePacote = Path.Combine(caminhos.DiretorioPacote, CaminhosApp.NomeChavePacote);
        // Banco novo nesta máquina: a chave do pacote é a que vale (substitui qualquer chave antiga guardada).
        if (File.Exists(chavePacote) && File.ReadAllText(chavePacote).Trim() is { Length: > 0 } chave)
            chaves.Salvar(chave);
        File.Copy(bancoPacote, caminhos.CaminhoBanco);
    }

    /// <summary>
    /// Primeiro uso a partir do .exe único: extrai o banco embutido para a pasta de dados e guarda a chave
    /// protegida pelo SO (DPAPI no Windows). O banco é gravado num arquivo temporário e só então renomeado,
    /// para que uma falha no meio não deixe um banco pela metade.
    /// </summary>
    private bool ImportarPacoteEmbutido()
    {
        using var banco = pacote.AbrirBanco();
        if (banco is null) return false;
        var chave = pacote.LerChave()
            ?? throw new InvalidOperationException("o executável traz o banco, mas não a chave dele.");

        Directory.CreateDirectory(Path.GetDirectoryName(caminhos.CaminhoBanco)!);
        var temporario = caminhos.CaminhoBanco + ".importando";
        try
        {
            using (var destino = File.Create(temporario)) banco.CopyTo(destino);
            // Banco novo nesta máquina: a chave embutida é a que vale (substitui qualquer chave antiga guardada).
            chaves.Salvar(chave);
            File.Move(temporario, caminhos.CaminhoBanco);
        }
        finally
        {
            if (File.Exists(temporario)) File.Delete(temporario);
        }
        return true;
    }

    /// <summary>Depois que a chave foi importada, apaga a cópia em texto que veio ao lado do executável.</summary>
    private void ApagarChaveDoPacote()
    {
        var chavePacote = Path.Combine(caminhos.DiretorioPacote, CaminhosApp.NomeChavePacote);
        try
        {
            if (File.Exists(chavePacote) && chaves.Obter() == File.ReadAllText(chavePacote).Trim())
                File.Delete(chavePacote);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

/// <summary>Gerador de banco: cria um .db cifrado, com migrations, uma única Empresa.</summary>
public class CriadorDeEmpresa
{
    public async Task<ResultadoCriacao> CriarAsync(string nome, string documento, string saida, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new CriacaoEmpresaException("Informe o nome da empresa.");
        if (string.IsNullOrWhiteSpace(documento)) throw new CriacaoEmpresaException("Informe o documento da empresa.");

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

public class EmpacotamentoException(string message) : Exception(message);

/// <summary>
/// Monta o pacote para o PC da empresa: copia o banco atual e grava a chave dele (escalas.chave) na pasta indicada,
/// ao lado do executável. O app importa os dois sozinho no primeiro uso.
/// </summary>
public class EmpacotadorInstalacao(CaminhosApp caminhos, IChaveStore chaves)
{
    public async Task<ResultadoCriacao> EmpacotarAsync(string pasta, CancellationToken ct = default)
    {
        if (!File.Exists(caminhos.CaminhoBanco))
            throw new EmpacotamentoException($"Banco não encontrado em {caminhos.CaminhoBanco}.");
        var chave = chaves.Obter()
            ?? (File.Exists(caminhos.CaminhoChaveEntrega) ? File.ReadAllText(caminhos.CaminhoChaveEntrega).Trim() : null);
        if (string.IsNullOrEmpty(chave))
            throw new EmpacotamentoException("A chave do banco não foi encontrada nesta máquina. Abra o app uma vez com esse banco antes de empacotar.");

        // Confere que a chave abre o banco antes de gerar o pacote.
        string? aviso;
        try
        {
            await using var db = new EscalasDbContext(BancoFabrica.Opcoes(caminhos.CaminhoBanco, chave));
            _ = (await db.Database.GetAppliedMigrationsAsync(ct)).Count();
            // Não entrega um banco que o app do cliente não conseguiria atualizar (filiais sem correspondência).
            aviso = await VerificacaoFiliaisFixas.VerificarAsync(db, await db.Database.GetPendingMigrationsAsync(ct), ct);
        }
        catch (SqliteException)
        {
            throw new EmpacotamentoException($"A chave desta máquina não abre o banco {caminhos.CaminhoBanco}.");
        }
        if (aviso is not null) throw new EmpacotamentoException(aviso);
        SqliteConnection.ClearAllPools();

        Directory.CreateDirectory(pasta);
        var destinoBanco = Path.Combine(pasta, CaminhosApp.NomeBancoPacote);
        var destinoChave = Path.Combine(pasta, CaminhosApp.NomeChavePacote);
        File.Copy(caminhos.CaminhoBanco, destinoBanco, overwrite: true);
        await File.WriteAllTextAsync(destinoChave, chave, ct);
        return new(destinoBanco, destinoChave);
    }
}
