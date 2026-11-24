using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;

namespace Escalas.Application.Calendario;

/// <summary>Horas de um funcionário em uma semana (segunda a domingo), pelo expediente de cada dia escalado.</summary>
/// <param name="Dias">Só os dias em que o funcionário está escalado, em ordem.</param>
public record HorasFuncionarioDto(
    int FuncionarioId,
    string Nome,
    string Setor,
    Filial Filial,
    DateOnly InicioSemana,
    IReadOnlyList<DiaEscalado> Dias,
    TotalHoras Totais,
    byte[]? Foto = null);

public record ResumoFilialDto(Filial Filial, string Nome, IReadOnlyList<HorasFuncionarioDto> Funcionarios)
{
    public TimeSpan Total => Funcionarios.Aggregate(TimeSpan.Zero, (s, f) => s + f.Totais.Total);
}

/// <param name="DiasSemExpediente">Datas da semana com alguém escalado em um dia da semana sem expediente cadastrado.</param>
public record ResumoSemanalDto(DateOnly Inicio, IReadOnlyList<ResumoFilialDto> Filiais, IReadOnlyList<DateOnly> DiasSemExpediente);

/// <summary>Horas trabalhadas na semana, calculadas pelo expediente geral de cada dia.</summary>
public interface IHorasService
{
    /// <summary>Null se o funcionário não existir.</summary>
    /// <param name="filialVista">
    /// Filial em que o funcionário foi clicado. Nula ou a filial dele: semana completa, incluindo os dias de cobertura.
    /// A filial extra dele: só os dias em que ele cobre essa filial (e as horas desses dias).
    /// </param>
    Task<HorasFuncionarioDto?> ObterFuncionarioAsync(int funcionarioId, DateOnly qualquerDiaDaSemana, Filial? filialVista = null, CancellationToken ct = default);

    /// <summary>Todos os funcionários ativos, agrupados por filial (na ordem fixa das filiais).</summary>
    Task<ResumoSemanalDto> ObterResumoAsync(DateOnly qualquerDiaDaSemana, CancellationToken ct = default);
}
