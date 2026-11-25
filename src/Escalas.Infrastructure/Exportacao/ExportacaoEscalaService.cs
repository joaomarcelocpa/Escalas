using ClosedXML.Excel;
using Escalas.Application.Calendario;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;

namespace Escalas.Infrastructure.Exportacao;

/// <summary>
/// Gera a planilha da escala semanal: uma aba por filial, uma linha por funcionário ativo e uma coluna por dia
/// (segunda a sábado). A célula traz o horário do funcionário (o personalizado dele, ou o expediente geral) nos dias em que o funcionário trabalha e "Descanso" nos demais.
/// A última coluna soma as horas da semana, já sem o almoço (regra de <see cref="RegraHoras"/>).
/// Cobertura de folga: o funcionário aparece só na aba da filial dele; o dia coberto mostra o horário e a filial coberta
/// (e conta horas). A aba da filial coberta não lista quem veio cobrir.
/// </summary>
public class ExportacaoEscalaService(IEscalaPadraoService escalas, IExpedienteService expedientes)
    : IExportacaoEscalaService
{
    private static readonly string[] NomesDias = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];
    private const int ColunaPrimeiroDia = 3; // A = funcionário, B = setor
    internal const string EscaladoSemHorario = "Escalado";
    internal const string Descanso = "Descanso";

    public async Task<byte[]> GerarPlanilhaAsync(CancellationToken ct = default)
    {
        var horarios = await expedientes.ObterTabelaAsync(ct);

        using var wb = new XLWorkbook();
        foreach (var filial in Filiais.Todas)
            MontarAba(wb.Worksheets.Add(filial.Nome), await escalas.ObterAsync(filial.Id, ct), horarios);

        using var saida = new MemoryStream();
        wb.SaveAs(saida);
        return saida.ToArray();
    }

    /// <summary>Horário do funcionário no dia (somando as horas) ou "Escalado" se o dia não tem horário.</summary>
    private static string Trabalho(int funcionarioId, DayOfWeek dia, TabelaHorarios horarios, ref TimeSpan horas)
    {
        if (horarios.Do(funcionarioId, dia) is not { } e) return EscaladoSemHorario;
        horas += RegraHoras.HorasTrabalhadas(e.Inicio, e.Fim);
        return RegraHoras.FormatarIntervalo(e.Inicio, e.Fim);
    }

    /// <summary>
    /// Horas da semana como texto ("40h", "37h30"), igual ao app. Não usa célula de duração: o Numbers (Mac) ignora o
    /// formato "[h]h mm" e mostra "1d 16h 0m 0s 0ms".
    /// </summary>
    private static void Total(IXLCell celula, TimeSpan horas)
    {
        celula.Value = RegraHoras.Formatar(horas);
        celula.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static void MontarAba(IXLWorksheet ws, SemanaPadraoDto semana, TabelaHorarios horarios)
    {
        var dias = semana.Dias;
        var ultimaColunaDia = ColunaPrimeiroDia + dias.Count - 1;
        var colunaHoras = ultimaColunaDia + 1;

        ws.Cell(1, 1).Value = "Funcionário";
        ws.Cell(1, 2).Value = "Setor";
        for (var i = 0; i < dias.Count; i++) ws.Cell(1, ColunaPrimeiroDia + i).Value = NomesDias[(int)dias[i].Dia];
        ws.Cell(1, colunaHoras).Value = "Horas na semana";
        ws.Range(1, 1, 1, colunaHoras).Style.Font.SetBold();

        // Um funcionário fixo está, em cada dia, escalado, cobrindo outra filial ou de folga: o primeiro dia lista todos.
        var funcionarios = dias.First().Setores
            .SelectMany(s => s.Escalados.Select(f => (f.Id, f.Nome, Setor: s.Nome)))
            .Concat(dias.First().Disponiveis.Select(f => (f.Id, f.Nome, Setor: f.SetorNome)))
            .Concat(dias.First().Fora.Select(f => (f.Id, f.Nome, f.Setor)))
            .OrderBy(f => f.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

        var linha = 2;
        foreach (var f in funcionarios)
        {
            ws.Cell(linha, 1).Value = f.Nome;
            ws.Cell(linha, 2).Value = f.Setor;
            var horas = TimeSpan.Zero;
            for (var i = 0; i < dias.Count; i++)
            {
                var celula = ws.Cell(linha, ColunaPrimeiroDia + i);
                var coberta = dias[i].Fora.FirstOrDefault(x => x.Id == f.Id)?.Filial;
                if (coberta is null && !dias[i].Setores.Any(s => s.Escalados.Any(x => x.Id == f.Id)))
                {
                    celula.Value = Descanso;
                    continue;
                }
                var texto = Trabalho(f.Id, dias[i].Dia, horarios, ref horas);
                celula.Value = coberta is { } c ? $"{texto} · {Filiais.Nome(c)}" : texto;
            }
            Total(ws.Cell(linha, colunaHoras), horas);
            linha++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns(1, colunaHoras).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 20);
        for (var c = ColunaPrimeiroDia; c <= ultimaColunaDia; c++) ws.Column(c).Width = Math.Max(ws.Column(c).Width, 13);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
    }
}
