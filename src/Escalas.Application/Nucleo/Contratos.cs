using Escalas.Domain.Entidades;

namespace Escalas.Application.Nucleo;

public interface IPasswordHasher
{
    string Hash(string senha);
    bool Verificar(string senha, string hash);
}

public interface IAutenticacaoService
{
    /// <summary>Retorna o usuário se login/senha conferem; caso contrário null.</summary>
    Task<Usuario?> EntrarAsync(string login, string senha, CancellationToken ct = default);
}

/// <summary>Guarda a chave de criptografia do banco fora do .db, de forma protegida pelo SO.</summary>
public interface IChaveStore
{
    string? Obter();
    void Salvar(string chave);
}

public interface IBackupService
{
    /// <summary>Copia o banco para o destino indicado (arquivo).</summary>
    void Exportar(string destino);

    /// <summary>Cria backup automático e mantém apenas as últimas N cópias. Retorna o caminho criado, ou null se não há banco.</summary>
    string? BackupAutomatico();

    /// <summary>Cria backup antes de migrar (não entra na retenção automática).</summary>
    string? BackupPreMigracao();
}

public enum SituacaoBanco { Pronto, BancoAusente, ChaveAusente, Erro }

public record ResultadoInicializacao(SituacaoBanco Situacao, string? Mensagem = null);

public interface IInicializadorBanco
{
    /// <summary>Verifica o banco, importa a chave (.chave) no primeiro uso, faz backup e aplica migrations pendentes.</summary>
    Task<ResultadoInicializacao> InicializarAsync(CancellationToken ct = default);
}

public class CriacaoEmpresaException(string message) : Exception(message);
