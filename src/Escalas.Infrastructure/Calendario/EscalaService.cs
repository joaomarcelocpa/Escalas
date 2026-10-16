using Escalas.Application.Calendario;
using Escalas.Application.Feriados;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Calendario;

public class EscalaService(IDbContextFactory<EscalasDbContext> factory, IFeriadoConsulta feriados) : IEscalaService
{
    public async Task<IReadOnlyList<Escalas.Application.Calendario.FilialResumo>> ListarFiliaisAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Filiais.AsNoTracking().OrderBy(f => f.Nome)
            .Select(f => new Escalas.Application.Calendario.FilialResumo(f.Id, f.Nome)).ToListAsync(ct);
    }

    public async Task<SemanaDto> ObterSemanaAsync(int filialId, DateOnly qualquerDiaDaSemana, CancellationToken ct = default)
    {
        var inicio = RegraEscala.InicioDaSemana(qualquerDiaDaSemana);
        var fim = inicio.AddDays(6);

        await using var db = await factory.CreateDbContextAsync(ct);

        var setores = await db.Setores.AsNoTracking().Where(s => s.FilialId == filialId)
            .OrderBy(s => s.Nome).ToListAsync(ct);
        var funcionarios = await db.Funcionarios.AsNoTracking()
            .Where(f => f.FilialId == filialId && f.Ativo).OrderBy(f => f.Nome).ToListAsync(ct);
        var idsFuncionarios = funcionarios.Select(f => f.Id).ToList();

        var escalas = await db.Escalas.AsNoTracking()
            .Where(e => e.Data >= inicio && e.Data <= fim && idsFuncionarios.Contains(e.FuncionarioId))
            .ToListAsync(ct);
        var fechados = (await db.DiasFilial.AsNoTracking()
            .Where(d => d.FilialId == filialId && d.Data >= inicio && d.Data <= fim && d.Fechado)
            .Select(d => d.Data).ToListAsync(ct)).ToHashSet();
        var listaFeriados = await feriados.ObterAsync(filialId, inicio, fim, ct);

        var setoresMinimo = setores.Select(s => new SetorMinimo(s.Id, s.Nome, s.MinimoFuncionarios)).ToList();
        var nomesSetor = setores.ToDictionary(s => s.Id, s => s.Nome);

        var dias = new List<DiaDto>();
        for (var i = 0; i < 7; i++)
        {
            var data = inicio.AddDays(i);
            var fechado = fechados.Contains(data);
            var nomeFeriado = listaFeriados.Where(f => f.Data == data).Select(f => f.Nome).FirstOrDefault();
            var status = fechado ? StatusDia.Fechado : nomeFeriado is not null ? StatusDia.Feriado : StatusDia.Normal;

            var idsEscalados = escalas.Where(e => e.Data == data).Select(e => e.FuncionarioId).ToHashSet();
            var contagem = funcionarios.Where(f => idsEscalados.Contains(f.Id))
                .GroupBy(f => f.SetorId).ToDictionary(g => g.Key, g => g.Count());
            var alertas = RegraMinimoSetor.Avaliar(setoresMinimo, contagem, fechado).Select(a => a.SetorId).ToHashSet();

            var setoresDia = setores.Select(s => new SetorDiaDto(
                s.Id, s.Nome, s.MinimoFuncionarios,
                funcionarios.Where(f => f.SetorId == s.Id && idsEscalados.Contains(f.Id))
                    .Select(f => new FuncionarioEscaladoDto(f.Id, f.Nome)).ToList(),
                alertas.Contains(s.Id))).ToList();

            var disponiveis = funcionarios.Where(f => !idsEscalados.Contains(f.Id))
                .Select(f => new FuncionarioDisponivelDto(f.Id, f.Nome, f.SetorId, nomesSetor.GetValueOrDefault(f.SetorId, "")))
                .ToList();

            dias.Add(new DiaDto(data, status, nomeFeriado, setoresDia, disponiveis));
        }

        return new SemanaDto(filialId, inicio, dias);
    }

    public async Task<ResultadoOperacao> EscalarAsync(int funcionarioId, DateOnly data, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var func = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(f => f.Id == funcionarioId, ct);
        if (func is null) return ResultadoOperacao.Falha("Funcionário não encontrado.");

        var r = await ValidarAsync(db, func, data, null, ct);
        if (!r.Ok) return ResultadoOperacao.Falha(r.Erro!);

        db.Escalas.Add(new Escala { FuncionarioId = funcionarioId, Data = data });
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    public async Task<ResultadoOperacao> RemoverAsync(int funcionarioId, DateOnly data, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var n = await db.Escalas.Where(e => e.FuncionarioId == funcionarioId && e.Data == data).ExecuteDeleteAsync(ct);
        return n > 0 ? ResultadoOperacao.Sucesso : ResultadoOperacao.Falha("Funcionário não estava escalado neste dia.");
    }

    public async Task<ResultadoOperacao> MoverAsync(int funcionarioId, DateOnly origem, DateOnly destino, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var func = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(f => f.Id == funcionarioId, ct);
        if (func is null) return ResultadoOperacao.Falha("Funcionário não encontrado.");

        var escala = await db.Escalas.FirstOrDefaultAsync(e => e.FuncionarioId == funcionarioId && e.Data == origem, ct);
        if (escala is null) return ResultadoOperacao.Falha("Funcionário não estava escalado no dia de origem.");

        var r = await ValidarAsync(db, func, destino, origem, ct);
        if (!r.Ok) return ResultadoOperacao.Falha(r.Erro!);

        escala.Data = destino;
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    public async Task<ResultadoOperacao> DefinirDiaFechadoAsync(int filialId, DateOnly data, bool fechado, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Filiais.AnyAsync(f => f.Id == filialId, ct)) return ResultadoOperacao.Falha("Filial não encontrada.");

        var dia = await db.DiasFilial.FirstOrDefaultAsync(d => d.FilialId == filialId && d.Data == data, ct);
        if (dia is null) db.DiasFilial.Add(new DiaFilial { FilialId = filialId, Data = data, Fechado = fechado });
        else dia.Fechado = fechado;

        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    /// <param name="origem">Quando informado, valida como movimentação.</param>
    private static async Task<ResultadoRegra> ValidarAsync(
        EscalasDbContext db, Funcionario func, DateOnly destino, DateOnly? origem, CancellationToken ct)
    {
        var fechado = await db.DiasFilial.AnyAsync(d => d.FilialId == func.FilialId && d.Data == destino && d.Fechado, ct);
        var jaEscalado = await db.Escalas.AnyAsync(e => e.FuncionarioId == func.Id && e.Data == destino, ct);
        var escalavel = new FuncionarioEscalavel(func.Id, func.FilialId, func.Ativo);

        return origem is { } o
            ? RegraEscala.ValidarMover(escalavel, func.FilialId, o, destino, fechado, jaEscalado)
            : RegraEscala.ValidarEscalar(escalavel, func.FilialId, fechado, jaEscalado);
    }
}
