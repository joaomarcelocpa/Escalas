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
        // Fixos da filial + quem tem esta filial como extra (pode estar cobrindo folga aqui em algum dia).
        var funcionarios = await db.Funcionarios.AsNoTracking().Include(f => f.Setor)
            .Where(f => f.Ativo && (f.Filial == filial || f.FilialExtra == filial)).OrderBy(f => f.Nome).ToListAsync(ct);
        var fixos = funcionarios.Where(f => f.Filial == filial).ToList();
        var externos = funcionarios.Where(f => f.Filial != filial).ToList();
        var idsFuncionarios = funcionarios.Select(f => f.Id).ToList();

        var excecoes = (await db.Escalas.AsNoTracking()
            .Where(e => e.Data >= inicio && e.Data <= fim && idsFuncionarios.Contains(e.FuncionarioId))
            .ToListAsync(ct)).ToDictionary(e => (e.FuncionarioId, e.Data), e => e.Removido);
        // Modelo semanal: (funcionário, dia da semana) → filial coberta naquele dia (null = própria filial).
        var modelo = (await db.EscalasPadrao.AsNoTracking()
            .Where(p => idsFuncionarios.Contains(p.FuncionarioId)).ToListAsync(ct))
            .ToDictionary(p => (p.FuncionarioId, p.DiaSemana), p => p.FilialCobertura);
        var definidos = await db.DiasFilial.AsNoTracking()
            .Where(d => d.Filial == filial && d.Data >= inicio && d.Data <= fim)
            .ToDictionaryAsync(d => d.Data, d => d.Fechado, ct);
        var listaFeriados = await feriados.ObterAsync(filial, inicio, fim, ct);

        var setoresMinimo = setores.Select(s => new SetorMinimo(s.Id, s.Nome, s.MinimoFuncionarios)).ToList();
        var nomesSetor = setores.ToDictionary(s => s.Id, s => s.Nome);
        // Quem cobre conta no mínimo do setor desta filial que tem o mesmo nome do setor dele (ex.: Caixa → Caixa).
        var setorPorNome = setores.GroupBy(s => Filiais.Normalizar(s.Nome)).ToDictionary(g => g.Key, g => g.First().Id);

        // Onde o funcionário trabalha na data: na própria filial, na filial que cobre, ou em lugar nenhum (folga).
        // Escalados = modelo semanal ± exceções da data (gravadas antes de o calendário virar somente leitura).
        Filial? Local(Funcionario f, DateOnly data)
        {
            var noModelo = modelo.TryGetValue((f.Id, data.DayOfWeek), out var cobertura);
            var trabalha = excecoes.TryGetValue((f.Id, data), out var removido) ? !removido : noModelo;
            if (!trabalha) return null;
            return noModelo && cobertura is { } c && c == f.FilialExtra && c != f.Filial ? c : f.Filial;
        }

        var dias = new List<DiaDto>();
        for (var i = 0; i < 7; i++)
        {
            var data = inicio.AddDays(i);
            var nomeFeriado = listaFeriados.Where(f => f.Data == data).Select(f => f.Nome).FirstOrDefault();
            var fechado = definidos.TryGetValue(data, out var definido) ? definido : FechadoPorPadrao(data, nomeFeriado is not null);
            var status = fechado ? StatusDia.Fechado : nomeFeriado is not null ? StatusDia.Feriado : StatusDia.Normal;

            var locais = funcionarios.ToDictionary(f => f.Id, f => Local(f, data));
            // Dia fechado não tem ninguém escalado (nem cobertura).
            var idsEscalados = fechado
                ? []
                : fixos.Where(f => locais[f.Id] == filial).Select(f => f.Id).ToHashSet();
            var cobrindo = fechado ? [] : externos.Where(f => locais[f.Id] == filial).ToList();
            var fora = fixos.Where(f => locais[f.Id] is { } local && local != filial).ToList();

            var contagem = fixos.Where(f => idsEscalados.Contains(f.Id))
                .GroupBy(f => f.SetorId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var c in cobrindo)
                if (setorPorNome.TryGetValue(Filiais.Normalizar(c.Setor?.Nome), out var setorId))
                    contagem[setorId] = contagem.GetValueOrDefault(setorId) + 1;
            var alertas = RegraMinimoSetor.Avaliar(setoresMinimo, contagem, fechado).Select(a => a.SetorId).ToHashSet();

            var setoresDia = setores.Select(s => new SetorDiaDto(
                s.Id, s.Nome, s.MinimoFuncionarios,
                fixos.Where(f => f.SetorId == s.Id && idsEscalados.Contains(f.Id))
                    .Select(f => new FuncionarioEscaladoDto(f.Id, f.Nome, f.Foto)).ToList(),
                alertas.Contains(s.Id))).ToList();

            // Folga: fixos que não trabalham no dia. Quem está cobrindo outra filial não está de folga.
            var disponiveis = fixos.Where(f => !idsEscalados.Contains(f.Id) && !fora.Contains(f))
                .Select(f => new FuncionarioDisponivelDto(f.Id, f.Nome, f.SetorId, nomesSetor.GetValueOrDefault(f.SetorId, ""), f.Foto))
                .ToList();

            dias.Add(new DiaDto(data, status, nomeFeriado, setoresDia, disponiveis)
            {
                Cobrindo = cobrindo.Select(f => new CoberturaDiaDto(f.Id, f.Nome, f.Setor?.Nome ?? "", f.Filial, f.Foto)).ToList(),
                Fora = fora.Select(f => new CoberturaDiaDto(f.Id, f.Nome, nomesSetor.GetValueOrDefault(f.SetorId, ""), locais[f.Id]!.Value, f.Foto)).ToList(),
            });
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
