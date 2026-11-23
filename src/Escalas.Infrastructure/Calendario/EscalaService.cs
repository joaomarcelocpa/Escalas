using Escalas.Application.Calendario;
using Escalas.Application.Feriados;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Calendario;

public class EscalaService(IDbContextFactory<EscalasDbContext> factory, IFeriadoConsulta feriados) : IEscalaService
{
    public async Task<SemanaDto> ObterSemanaAsync(Filial filial, DateOnly qualquerDiaDaSemana, CancellationToken ct = default)
    {
        var inicio = RegraEscala.InicioDaSemana(qualquerDiaDaSemana);
        var fim = inicio.AddDays(6);

        await using var db = await factory.CreateDbContextAsync(ct);

        var setores = await db.Setores.AsNoTracking().Where(s => s.Filial == filial)
            .OrderBy(s => s.Nome).ToListAsync(ct);
        var funcionarios = await db.Funcionarios.AsNoTracking()
            .Where(f => f.Filial == filial && f.Ativo).OrderBy(f => f.Nome).ToListAsync(ct);
        var idsFuncionarios = funcionarios.Select(f => f.Id).ToList();

        var excecoes = (await db.Escalas.AsNoTracking()
            .Where(e => e.Data >= inicio && e.Data <= fim && idsFuncionarios.Contains(e.FuncionarioId))
            .ToListAsync(ct)).ToDictionary(e => (e.FuncionarioId, e.Data), e => e.Removido);
        var modelo = (await db.EscalasPadrao.AsNoTracking()
            .Where(p => idsFuncionarios.Contains(p.FuncionarioId)).ToListAsync(ct))
            .Select(p => (p.FuncionarioId, p.DiaSemana)).ToHashSet();
        var definidos = await db.DiasFilial.AsNoTracking()
            .Where(d => d.Filial == filial && d.Data >= inicio && d.Data <= fim)
            .ToDictionaryAsync(d => d.Data, d => d.Fechado, ct);
        var listaFeriados = await feriados.ObterAsync(filial, inicio, fim, ct);

        var setoresMinimo = setores.Select(s => new SetorMinimo(s.Id, s.Nome, s.MinimoFuncionarios)).ToList();
        var nomesSetor = setores.ToDictionary(s => s.Id, s => s.Nome);

        var dias = new List<DiaDto>();
        for (var i = 0; i < 7; i++)
        {
            var data = inicio.AddDays(i);
            var nomeFeriado = listaFeriados.Where(f => f.Data == data).Select(f => f.Nome).FirstOrDefault();
            var fechado = definidos.TryGetValue(data, out var definido) ? definido : FechadoPorPadrao(data, nomeFeriado is not null);
            var status = fechado ? StatusDia.Fechado : nomeFeriado is not null ? StatusDia.Feriado : StatusDia.Normal;

            // Escalados do dia = modelo semanal ± exceções da data (gravadas antes de o calendário virar somente leitura).
            // Dia fechado não tem ninguém escalado.
            var idsEscalados = fechado
                ? []
                : funcionarios.Where(f => excecoes.TryGetValue((f.Id, data), out var removido)
                        ? !removido
                        : modelo.Contains((f.Id, data.DayOfWeek)))
                    .Select(f => f.Id).ToHashSet();
            var contagem = funcionarios.Where(f => idsEscalados.Contains(f.Id))
                .GroupBy(f => f.SetorId).ToDictionary(g => g.Key, g => g.Count());
            var alertas = RegraMinimoSetor.Avaliar(setoresMinimo, contagem, fechado).Select(a => a.SetorId).ToHashSet();

            var setoresDia = setores.Select(s => new SetorDiaDto(
                s.Id, s.Nome, s.MinimoFuncionarios,
                funcionarios.Where(f => f.SetorId == s.Id && idsEscalados.Contains(f.Id))
                    .Select(f => new FuncionarioEscaladoDto(f.Id, f.Nome, f.Foto)).ToList(),
                alertas.Contains(s.Id))).ToList();

            var disponiveis = funcionarios.Where(f => !idsEscalados.Contains(f.Id))
                .Select(f => new FuncionarioDisponivelDto(f.Id, f.Nome, f.SetorId, nomesSetor.GetValueOrDefault(f.SetorId, ""), f.Foto))
                .ToList();

            dias.Add(new DiaDto(data, status, nomeFeriado, setoresDia, disponiveis));
        }

        return new SemanaDto(filial, inicio, dias);
    }

    public async Task<ResultadoOperacao> DefinirDiaFechadoAsync(Filial filial, DateOnly data, bool fechado, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!Filiais.Existe(filial)) return ResultadoOperacao.Falha("Filial não encontrada.");

        var dia = await db.DiasFilial.FirstOrDefaultAsync(d => d.Filial == filial && d.Data == data, ct);
        if (dia is null) db.DiasFilial.Add(new DiaFilial { Filial = filial, Data = data, Fechado = fechado });
        else dia.Fechado = fechado;

        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    /// <summary>Domingo e feriado vêm fechados até que alguém reabra o dia explicitamente.</summary>
    private static bool FechadoPorPadrao(DateOnly data, bool feriado) => feriado || data.DayOfWeek == DayOfWeek.Sunday;
}
