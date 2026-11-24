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
        // Fixos da filial + quem tem esta filial como extra (cobre folgas aqui).
        var funcionarios = await db.Funcionarios.AsNoTracking().Include(f => f.Setor)
            .Where(f => f.Ativo && (f.Filial == filial || f.FilialExtra == filial)).OrderBy(f => f.Nome).ToListAsync(ct);
        var fixos = funcionarios.Where(f => f.Filial == filial).ToList();
        var externos = funcionarios.Where(f => f.Filial != filial).ToList();
        var ids = funcionarios.Select(f => f.Id).ToList();
        var modelo = (await db.EscalasPadrao.AsNoTracking().Where(p => ids.Contains(p.FuncionarioId)).ToListAsync(ct))
            .ToDictionary(p => (p.FuncionarioId, p.DiaSemana), p => p.FilialCobertura);
        var nomesSetor = setores.ToDictionary(s => s.Id, s => s.Nome);

        bool Trabalha(Funcionario f, DayOfWeek dia) => modelo.TryGetValue((f.Id, dia), out var c) && c is null;
        Filial? Cobre(Funcionario f, DayOfWeek dia) => modelo.TryGetValue((f.Id, dia), out var c) ? c : null;

        var dias = DiasUteis.Select(dia =>
        {
            var doDia = fixos.Where(f => Trabalha(f, dia)).ToList();
            var fora = fixos.Where(f => Cobre(f, dia) is not null).ToList();
            var setoresDia = setores.Select(s => new SetorDiaDto(
                s.Id, s.Nome, 0,
                doDia.Where(f => f.SetorId == s.Id).Select(f => new FuncionarioEscaladoDto(f.Id, f.Nome, f.Foto)).ToList(),
                false)).ToList();
            // Folga: não trabalha neste dia. Quem está cobrindo outra filial não entra aqui.
            var disponiveis = fixos.Where(f => !modelo.ContainsKey((f.Id, dia)))
                .Select(f => new FuncionarioDisponivelDto(f.Id, f.Nome, f.SetorId, nomesSetor.GetValueOrDefault(f.SetorId, ""), f.Foto))
                .ToList();
            var cobrindo = externos.Where(f => Cobre(f, dia) == filial).ToList();
            // Só oferece cobertura quando algum fixo desta filial folga no dia; o candidato precisa trabalhar na filial dele nesse dia.
            var candidatos = disponiveis.Count == 0 ? [] : externos.Where(f => Trabalha(f, dia)).ToList();

            return new DiaPadraoDto(dia, setoresDia, disponiveis)
            {
                Cobrindo = cobrindo.Select(f => new CoberturaDiaDto(f.Id, f.Nome, f.Setor?.Nome ?? "", f.Filial, f.Foto)).ToList(),
                Fora = fora.Select(f => new CoberturaDiaDto(f.Id, f.Nome, nomesSetor.GetValueOrDefault(f.SetorId, ""), Cobre(f, dia)!.Value, f.Foto)).ToList(),
                Candidatos = candidatos.Select(f => new CoberturaDiaDto(f.Id, f.Nome, f.Setor?.Nome ?? "", f.Filial, f.Foto)).ToList(),
            };
        }).ToList();

        return new SemanaPadraoDto(filial, dias);
    }

    public async Task<ResultadoOperacao> AlocarAsync(int funcionarioId, DayOfWeek dia, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var erro = await ValidarAsync(db, funcionarioId, dia, ct);
        if (erro is not null) return ResultadoOperacao.Falha(erro);
        var existente = await db.EscalasPadrao.AsNoTracking()
            .FirstOrDefaultAsync(p => p.FuncionarioId == funcionarioId && p.DiaSemana == dia, ct);
        if (existente is not null)
            return ResultadoOperacao.Falha(existente.FilialCobertura is { } coberta
                ? $"Funcionário está cobrindo {Filiais.Nome(coberta)} neste dia."
                : "Funcionário já está alocado neste dia.");

        db.EscalasPadrao.Add(new EscalaPadrao { FuncionarioId = funcionarioId, DiaSemana = dia });
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    public async Task<ResultadoOperacao> DesalocarAsync(int funcionarioId, DayOfWeek dia, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var n = await db.EscalasPadrao
            .Where(p => p.FuncionarioId == funcionarioId && p.DiaSemana == dia && p.FilialCobertura == null)
            .ExecuteDeleteAsync(ct);
        return n > 0 ? ResultadoOperacao.Sucesso : ResultadoOperacao.Falha("Funcionário não estava alocado neste dia.");
    }

    public async Task<ResultadoOperacao> MoverAsync(int funcionarioId, DayOfWeek origem, DayOfWeek destino, CancellationToken ct = default)
    {
        if (origem == destino) return ResultadoOperacao.Sucesso;
        await using var db = await factory.CreateDbContextAsync(ct);
        var erro = await ValidarAsync(db, funcionarioId, destino, ct);
        if (erro is not null) return ResultadoOperacao.Falha(erro);

        var linha = await db.EscalasPadrao.FirstOrDefaultAsync(
            p => p.FuncionarioId == funcionarioId && p.DiaSemana == origem && p.FilialCobertura == null, ct);
        if (linha is null) return ResultadoOperacao.Falha("Funcionário não estava alocado no dia de origem.");

        var noDestino = await db.EscalasPadrao.AsNoTracking()
            .FirstOrDefaultAsync(p => p.FuncionarioId == funcionarioId && p.DiaSemana == destino, ct);
        if (noDestino?.FilialCobertura is { } coberta)
            return ResultadoOperacao.Falha($"Funcionário está cobrindo {Filiais.Nome(coberta)} no dia de destino.");
        if (noDestino is not null)
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

    public async Task<ResultadoOperacao> CobrirAsync(int funcionarioId, Filial filial, DayOfWeek dia, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var erro = await ValidarAsync(db, funcionarioId, dia, ct);
        if (erro is not null) return ResultadoOperacao.Falha(erro);
        var func = await db.Funcionarios.AsNoTracking().FirstAsync(f => f.Id == funcionarioId, ct);
        if (!Filiais.Existe(filial)) return ResultadoOperacao.Falha("Filial não encontrada.");
        if (func.FilialExtra != filial || func.Filial == filial)
            return ResultadoOperacao.Falha($"{func.Nome} não tem {Filiais.Nome(filial)} como filial extra.");

        var linha = await db.EscalasPadrao.FirstOrDefaultAsync(p => p.FuncionarioId == funcionarioId && p.DiaSemana == dia, ct);
        if (linha is null)
            return ResultadoOperacao.Falha(
                $"{func.Nome} está de folga neste dia. A cobertura usa um dia em que ele trabalha na {Filiais.Nome(func.Filial)}.");
        if (linha.FilialCobertura == filial)
            return ResultadoOperacao.Falha($"{func.Nome} já cobre {Filiais.Nome(filial)} neste dia.");

        linha.FilialCobertura = filial; // sai da própria filial neste dia e vai para a filial coberta
        await db.SaveChangesAsync(ct);
        return ResultadoOperacao.Sucesso;
    }

    public async Task<ResultadoOperacao> RemoverCoberturaAsync(int funcionarioId, DayOfWeek dia, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var linha = await db.EscalasPadrao.FirstOrDefaultAsync(
            p => p.FuncionarioId == funcionarioId && p.DiaSemana == dia && p.FilialCobertura != null, ct);
        if (linha is null) return ResultadoOperacao.Falha("Funcionário não estava cobrindo neste dia.");
        linha.FilialCobertura = null; // volta a trabalhar na própria filial
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
