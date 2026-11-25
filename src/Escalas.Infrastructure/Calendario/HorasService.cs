using Escalas.Application.Calendario;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Calendario;

/// <summary>
/// Junta quem está escalado em cada dia (mesma regra do calendário: modelo semanal, exceções, dias fechados e feriados)
/// com o horário que vale para o funcionário no dia: o personalizado dele, se houver, ou o expediente geral do dia da semana.
/// </summary>
public class HorasService(
    IDbContextFactory<EscalasDbContext> factory,
    IEscalaService escalas,
    IExpedienteService expedientes,
    TimeProvider relogio) : IHorasService
{
    public async Task<HorasFuncionarioDto?> ObterFuncionarioAsync(int funcionarioId, DateOnly qualquerDiaDaSemana, Filial? filialVista = null, CancellationToken ct = default)
    {
        Funcionario? func;
        await using (var db = await factory.CreateDbContextAsync(ct))
            func = await db.Funcionarios.AsNoTracking().Include(f => f.Setor).FirstOrDefaultAsync(f => f.Id == funcionarioId, ct);
        if (func is null) return null;

        // Visto na filial extra: só os dias em que ele cobre lá.
        if (filialVista is { } vista && vista != func.Filial && vista == func.FilialExtra)
        {
            var coberta = await escalas.ObterSemanaAsync(vista, qualquerDiaDaSemana, ct);
            return MontarCobertura(func, coberta, await expedientes.ObterTabelaAsync(ct));
        }

        // Própria filial e, se houver, a filial extra (dias em que cobre folga lá também contam horas).
        var semanas = new Dictionary<Filial, SemanaDto>
        {
            [func.Filial] = await escalas.ObterSemanaAsync(func.Filial, qualquerDiaDaSemana, ct),
        };
        if (func.FilialExtra is { } extra && extra != func.Filial)
            semanas[extra] = await escalas.ObterSemanaAsync(extra, qualquerDiaDaSemana, ct);
        return Montar(func, semanas, await expedientes.ObterTabelaAsync(ct));
    }

    public async Task<ResumoSemanalDto> ObterResumoAsync(DateOnly qualquerDiaDaSemana, CancellationToken ct = default)
    {
        var inicio = RegraEscala.InicioDaSemana(qualquerDiaDaSemana);
        var horarios = await expedientes.ObterTabelaAsync(ct);

        List<Funcionario> ativos;
        await using (var db = await factory.CreateDbContextAsync(ct))
            ativos = await db.Funcionarios.AsNoTracking().Include(f => f.Setor).Where(f => f.Ativo).ToListAsync(ct);
        ativos = ativos.OrderBy(f => f.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

        var semanas = new Dictionary<Filial, SemanaDto>();
        foreach (var filial in Filiais.Todas)
            semanas[filial.Id] = await escalas.ObterSemanaAsync(filial.Id, inicio, ct);

        var filiais = new List<ResumoFilialDto>();
        foreach (var filial in Filiais.Todas)
        {
            // Todos os ativos da filial aparecem, mesmo sem dia escalado. Quem cobre folga em outra filial aparece só aqui,
            // na filial de origem, com o dia de cobertura marcado (a filial coberta não lista o cobridor).
            var linhas = ativos.Where(f => f.Filial == filial.Id)
                .Select(f => Montar(f, semanas, horarios))
                .ToList();
            filiais.Add(new ResumoFilialDto(filial.Id, filial.Nome, linhas));
        }

        // Só interessam as datas em que alguém está escalado e o dia da semana não tem expediente.
        var semExpediente = filiais.SelectMany(f => f.Funcionarios).SelectMany(f => f.Dias)
            .Where(d => !d.TemExpediente).Select(d => d.Data).Distinct().Order().ToList();
        return new ResumoSemanalDto(inicio, filiais, semExpediente);
    }

    /// <summary>Dias da semana em que o funcionário trabalha: na própria filial ou cobrindo a filial extra.</summary>
    private HorasFuncionarioDto Montar(Funcionario func, IReadOnlyDictionary<Filial, SemanaDto> semanas, TabelaHorarios horarios)
    {
        var propria = semanas[func.Filial];
        SemanaDto? extra = func.FilialExtra is { } fe && fe != func.Filial ? semanas.GetValueOrDefault(fe) : null;
        var dias = new List<DiaEscalado>();
        for (var i = 0; i < propria.Dias.Count; i++)
        {
            var d = propria.Dias[i];
            if (d.Setores.Any(s => s.Escalados.Any(f => f.Id == func.Id)))
                dias.Add(horarios.Dia(func.Id, d.Data));
            else if (extra?.Dias.FirstOrDefault(x => x.Data == d.Data) is { } coberto && coberto.Cobrindo.Any(f => f.Id == func.Id))
                dias.Add(horarios.Dia(func.Id, d.Data, extra.Filial));
        }
        return Criar(func, propria.Inicio, dias);
    }

    /// <summary>Só os dias em que o funcionário cobre a filial desta semana.</summary>
    private HorasFuncionarioDto MontarCobertura(Funcionario func, SemanaDto coberta, TabelaHorarios horarios)
    {
        var dias = coberta.Dias.Where(d => d.Cobrindo.Any(f => f.Id == func.Id))
            .Select(d => horarios.Dia(func.Id, d.Data, coberta.Filial)).ToList();
        return Criar(func, coberta.Inicio, dias);
    }

    private HorasFuncionarioDto Criar(Funcionario func, DateOnly inicio, List<DiaEscalado> dias)
    {
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);
        return new HorasFuncionarioDto(func.Id, func.Nome, func.Setor?.Nome ?? "", func.Filial, inicio, dias, RegraHoras.Totalizar(dias, hoje), func.Foto);
    }
}
