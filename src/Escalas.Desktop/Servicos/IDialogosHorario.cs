using Escalas.Domain.Entidades;

namespace Escalas.Desktop.Servicos;

/// <summary>Janelas da gestão de horários: edição do expediente (escala semanal), horas e resumo (calendário).</summary>
public interface IDialogosHorario
{
    /// <summary>Edita o expediente por dia da semana (escala semanal). True se algo foi salvo.</summary>
    Task<bool> EditarExpedienteAsync();

    /// <param name="filial">Filial em que o funcionário foi clicado: na filial extra, mostra só os dias de cobertura.</param>
    Task MostrarHorasFuncionarioAsync(int funcionarioId, DateOnly inicioSemana, Filial filial);

    Task MostrarResumoSemanalAsync(DateOnly inicioSemana);
}
