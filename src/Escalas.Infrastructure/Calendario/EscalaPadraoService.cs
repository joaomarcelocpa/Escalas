using Escalas.Application.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Calendario;

public class EscalaPadraoService(IDbContextFactory<EscalasDbContext> factory) : IEscalaPadraoService
{
    public static readonly DayOfWeek[] DiasUteis =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    public async Task<SemanaPadraoDto> ObterAsync(Filial filial, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var setores = await db.Setores.AsNoTracking().Where(s => s.Filial == filial).OrderBy(s => s.Nome).ToListAsync(ct);
        var funcionarios = await db.Funcionarios.AsNoTracking()
            .Where(f => f.Filial == filial && f.Ativo).OrderBy(f => f.Nome).ToListAsync(ct);
        var ids = funcionarios.Select(f => f.Id).ToList();
        var alocados = (await db.EscalasPadrao.AsNoTracking().Where(p => ids.Contains(p.FuncionarioId)).ToListAsync(ct))
            .Select(p => (p.FuncionarioId, p.DiaSemana)).ToHashSet();
        var nomesSetor = setores.ToDictionary(s => s.Id, s => s.Nome);

        var dias = DiasUteis.Select(dia =>
        {
            var doDia = funcionarios.Where(f => alocados.Contains((f.Id, dia))).ToList();
            var setoresDia = setores.Select(s => new SetorDiaDto(
                s.Id, s.Nome, 0,
                doDia.Where(f => f.SetorId == s.Id).Select(f => new FuncionarioEscaladoDto(f.Id, f.Nome, f.Foto)).ToList(),
                false)).ToList();
            var disponiveis = funcionarios.Where(f => !alocados.Contains((f.Id, dia)))
                .Select(f => new FuncionarioDisponivelDto(f.Id, f.Nome, f.SetorId, nomesSetor.GetValueOrDefault(f.SetorId, ""), f.Foto))
                .ToList();
            return new DiaPadraoDto(dia, setoresDia, disponiveis);
        }).ToList();

        return new SemanaPadraoDto(filial, dias);
    }

    public async Task<ResultadoOperacao> AlocarAsync(int funcionarioId, DayOfWeek dia, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var erro = await ValidarAsync(db, funcionarioId, dia, ct);
        if (erro is not null) return ResultadoOperacao.Falha(erro);
        if (await db.EscalasPadrao.AnyAsync(p => p.FuncionarioId == funcionarioId && p.DiaSemana == dia, ct))
            return ResultadoOperacao.Falha("Funcionário já está alocado neste dia.");

        db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = funcionarioId, DiaSemana = dia });
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    public async Task<ResultadoOperacao> DesalocarAsync(int funcionarioId, DayOfWeek dia, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var n = await db.EscalasPadrao.Where(p => p.FuncionarioId == funcionarioId && p.DiaSemana == dia).ExecuteDeleteAsync(ct);
        return n > 0 ? ResultadoOperacao.Sucesso : ResultadoOperacao.Falha("Funcionário não estava alocado neste dia.");
    }

    public async Task<ResultadoOperacao> MoverAsync(int funcionarioId, DayOfWeek origem, DayOfWeek destino, CancellationToken ct = default)
    {
        if (origem == destino) return ResultadoOperacao.Sucesso;
        await using var db = await factory.CreateDbContextAsync(ct);
        var erro = await ValidarAsync(db, funcionarioId, destino, ct);
        if (erro is not null) return ResultadoOperacao.Falha(erro);

        var linha = await db.EscalasPadrao.FirstOrDefaultAsync(p => p.FuncionarioId == funcionarioId && p.DiaSemana == origem, ct);
        if (linha is null) return ResultadoOperacao.Falha("Funcionário não estava alocado no dia de origem.");

        if (await db.EscalasPadrao.AnyAsync(p => p.FuncionarioId == funcionarioId && p.DiaSemana == destino, ct))
            db.EscalasPadrao.Remove(linha); // já estava no destino: apenas sai da origem
        else
            linha.DiaSemana = destino;
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    public async Task<ResultadoOperacao> AlocarTodosOsDiasAsync(int funcionarioId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var erro = await ValidarAsync(db, funcionarioId, DayOfWeek.Monday, ct);
        if (erro is not null) return ResultadoOperacao.Falha(erro);

        var existentes = await db.EscalasPadrao.Where(p => p.FuncionarioId == funcionarioId).Select(p => p.DiaSemana).ToListAsync(ct);
        foreach (var dia in DiasUteis.Except(existentes))
            db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = funcionarioId, DiaSemana = dia });
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    private static async Task<string?> ValidarAsync(EscalasDbContext db, int funcionarioId, DayOfWeek dia, CancellationToken ct)
    {
        if (!DiasUteis.Contains(dia)) return "O modelo semanal cobre apenas segunda a sábado.";
        var func = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(f => f.Id == funcionarioId, ct);
        if (func is null) return "Funcionário não encontrado.";
        return func.Ativo ? null : "Funcionário inativo.";
    }
}
