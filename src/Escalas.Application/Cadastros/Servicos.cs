using Escalas.Domain.Entidades;

namespace Escalas.Application.Cadastros;

public interface ISetorService
{
    Task<IReadOnlyList<Setor>> ListarAsync(Filial filial, CancellationToken ct = default);
    Task<Setor> SalvarAsync(Setor setor, CancellationToken ct = default);
    Task ExcluirAsync(int id, CancellationToken ct = default);
}

public interface IFuncionarioService
{
    /// <summary>Lista funcionários (com Setor carregado); filtra por filial quando informada.</summary>
    Task<IReadOnlyList<Funcionario>> ListarAsync(Filial? filial = null, CancellationToken ct = default);
    Task<Funcionario> SalvarAsync(Funcionario funcionario, CancellationToken ct = default);
    /// <summary>Exclui; se já houver escalas lançadas, lança <see cref="CadastroException"/> sugerindo desativar.</summary>
    Task ExcluirAsync(int id, CancellationToken ct = default);
}
