using Escalas.Domain.Entidades;

namespace Escalas.Application.Cadastros;

public interface IFilialService
{
    Task<IReadOnlyList<Filial>> ListarAsync(CancellationToken ct = default);
    /// <summary>Cria (Id = 0) ou atualiza. Lança <see cref="CadastroException"/> se inválida.</summary>
    Task<Filial> SalvarAsync(Filial filial, CancellationToken ct = default);
    Task ExcluirAsync(int id, CancellationToken ct = default);
}

public interface ISetorService
{
    Task<IReadOnlyList<Setor>> ListarAsync(int filialId, CancellationToken ct = default);
    Task<Setor> SalvarAsync(Setor setor, CancellationToken ct = default);
    Task ExcluirAsync(int id, CancellationToken ct = default);
}

public interface IFuncionarioService
{
    /// <summary>Lista funcionários (com Setor carregado); filtra por filial quando informada.</summary>
    Task<IReadOnlyList<Funcionario>> ListarAsync(int? filialId = null, CancellationToken ct = default);
    Task<Funcionario> SalvarAsync(Funcionario funcionario, CancellationToken ct = default);
    /// <summary>Exclui; se já houver escalas lançadas, lança <see cref="CadastroException"/> sugerindo desativar.</summary>
    Task ExcluirAsync(int id, CancellationToken ct = default);
}
