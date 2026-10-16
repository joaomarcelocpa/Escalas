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

        var excecoes = (await db.Escalas.AsNoTracking()
            .Where(e => e.Data >= inicio && e.Data <= fim && idsFuncionarios.Contains(e.FuncionarioId))
            .ToListAsync(ct)).ToDictionary(e => (e.FuncionarioId, e.Data), e => e.Removido);
        var modelo = (await db.EscalasPadrao.AsNoTracking()
            .Where(p => idsFuncionarios.Contains(p.FuncionarioId)).ToListAsync(ct))
            .Select(p => (p.FuncionarioId, p.DiaSemana)).ToHashSet();
        var definidos = await db.DiasFilial.AsNoTracking()
            .Where(d => d.FilialId == filialId && d.Data >= inicio && d.Data <= fim)
            .ToDictionaryAsync(d => d.Data, d => d.Fechado, ct);
        var listaFeriados = await feriados.ObterAsync(filialId, inicio, fim, ct);

        var setoresMinimo = setores.Select(s => new SetorMinimo(s.Id, s.Nome, s.MinimoFuncionarios)).ToList();
        var nomesSetor = setores.ToDictionary(s => s.Id, s => s.Nome);

        var dias = new List<DiaDto>();
        for (var i = 0; i < 7; i++)
        {
            var data = inicio.AddDays(i);
            var nomeFeriado = listaFeriados.Where(f => f.Data == data).Select(f => f.Nome).FirstOrDefault();
            var fechado = definidos.TryGetValue(data, out var definido) ? definido : FechadoPorPadrao(data, nomeFeriado is not null);
            var status = fechado ? StatusDia.Fechado : nomeFeriado is not null ? StatusDia.Feriado : StatusDia.Normal;

            // Escalados do dia = modelo semanal ± exceções da data. Dia fechado não tem ninguém escalado.
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

        return new SemanaDto(filialId, inicio, dias);
    }

    public async Task<ResultadoOperacao> EscalarAsync(int funcionarioId, DateOnly data, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var func = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(f => f.Id == funcionarioId, ct);
        if (func is null) return ResultadoOperacao.Falha("Funcionário não encontrado.");

        var r = await ValidarAsync(db, func, data, null, ct);
        if (!r.Ok) return ResultadoOperacao.Falha(r.Erro!);

        await IncluirNoDiaAsync(db, funcionarioId, data, ct);
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    public async Task<ResultadoOperacao> RemoverAsync(int funcionarioId, DateOnly data, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await EstaEscaladoAsync(db, funcionarioId, data, ct))
            return ResultadoOperacao.Falha("Funcionário não estava escalado neste dia.");

        await RetirarDoDiaAsync(db, funcionarioId, data, ct);
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    public async Task<ResultadoOperacao> MoverAsync(int funcionarioId, DateOnly origem, DateOnly destino, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var func = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(f => f.Id == funcionarioId, ct);
        if (func is null) return ResultadoOperacao.Falha("Funcionário não encontrado.");

        if (!await EstaEscaladoAsync(db, funcionarioId, origem, ct))
            return ResultadoOperacao.Falha("Funcionário não estava escalado no dia de origem.");

        var r = await ValidarAsync(db, func, destino, origem, ct);
        if (!r.Ok) return ResultadoOperacao.Falha(r.Erro!);

        await RetirarDoDiaAsync(db, funcionarioId, origem, ct);
        await IncluirNoDiaAsync(db, funcionarioId, destino, ct);
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

    /// <summary>Presença efetiva no dia: a exceção da data, se existir, senão o modelo semanal.</summary>
    private static async Task<bool> EstaEscaladoAsync(EscalasDbContext db, int funcionarioId, DateOnly data, CancellationToken ct)
    {
        var excecao = await db.Escalas.AsNoTracking()
            .Where(e => e.FuncionarioId == funcionarioId && e.Data == data)
            .Select(e => (bool?)e.Removido).FirstOrDefaultAsync(ct);
        if (excecao is not null) return !excecao.Value;
        return await NoModeloAsync(db, funcionarioId, data, ct);
    }

    private static Task<bool> NoModeloAsync(EscalasDbContext db, int funcionarioId, DateOnly data, CancellationToken ct) =>
        db.EscalasPadrao.AnyAsync(p => p.FuncionarioId == funcionarioId && p.DiaSemana == data.DayOfWeek, ct);

    /// <summary>Inclui no dia: se o modelo já cobre, basta apagar a exceção de remoção; senão registra uma inclusão.</summary>
    private static async Task IncluirNoDiaAsync(EscalasDbContext db, int funcionarioId, DateOnly data, CancellationToken ct)
    {
        var linha = await db.Escalas.FirstOrDefaultAsync(e => e.FuncionarioId == funcionarioId && e.Data == data, ct);
        if (await NoModeloAsync(db, funcionarioId, data, ct))
        {
            if (linha is not null) db.Escalas.Remove(linha);
        }
        else if (linha is null) db.Escalas.Add(new Escala { FuncionarioId = funcionarioId, Data = data });
        else linha.Removido = false;
    }

    /// <summary>Retira do dia: se o modelo cobre, registra uma remoção; senão apaga a inclusão pontual.</summary>
    private static async Task RetirarDoDiaAsync(EscalasDbContext db, int funcionarioId, DateOnly data, CancellationToken ct)
    {
        var linha = await db.Escalas.FirstOrDefaultAsync(e => e.FuncionarioId == funcionarioId && e.Data == data, ct);
        if (await NoModeloAsync(db, funcionarioId, data, ct))
        {
            if (linha is null) db.Escalas.Add(new Escala { FuncionarioId = funcionarioId, Data = data, Removido = true });
            else linha.Removido = true;
        }
        else if (linha is not null) db.Escalas.Remove(linha);
    }

    /// <summary>Domingo e feriado vêm fechados até que alguém reabra o dia explicitamente.</summary>
    private static bool FechadoPorPadrao(DateOnly data, bool feriado) => feriado || data.DayOfWeek == DayOfWeek.Sunday;

    /// <param name="origem">Quando informado, valida como movimentação.</param>
    private async Task<ResultadoRegra> ValidarAsync(
        EscalasDbContext db, Funcionario func, DateOnly destino, DateOnly? origem, CancellationToken ct)
    {
        var definido = await db.DiasFilial.Where(d => d.FilialId == func.FilialId && d.Data == destino)
            .Select(d => (bool?)d.Fechado).FirstOrDefaultAsync(ct);
        var feriado = definido is null && (await feriados.ObterAsync(func.FilialId, destino, destino, ct)).Count > 0;
        var fechado = definido ?? FechadoPorPadrao(destino, feriado);
        var jaEscalado = await EstaEscaladoAsync(db, func.Id, destino, ct);
        var escalavel = new FuncionarioEscalavel(func.Id, func.FilialId, func.Ativo);

        return origem is { } o
            ? RegraEscala.ValidarMover(escalavel, func.FilialId, o, destino, fechado, jaEscalado)
            : RegraEscala.ValidarEscalar(escalavel, func.FilialId, fechado, jaEscalado);
    }
}
