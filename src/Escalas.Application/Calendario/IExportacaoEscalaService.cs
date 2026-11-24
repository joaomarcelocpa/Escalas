namespace Escalas.Application.Calendario;

/// <summary>Exporta o modelo semanal (escala semanal) de todas as filiais.</summary>
public interface IExportacaoEscalaService
{
    /// <summary>Planilha .xlsx com uma aba por filial: quem trabalha em cada dia, expediente e horas por funcionário.</summary>
    Task<byte[]> GerarPlanilhaAsync(CancellationToken ct = default);
}
