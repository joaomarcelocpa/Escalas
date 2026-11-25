using Escalas.Application.Cadastros;
using Escalas.Domain.Calendario;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Cadastros;

public class FuncionarioService(IDbContextFactory<EscalasDbContext> factory) : IFuncionarioService
{
    public async Task<IReadOnlyList<Funcionario>> ListarAsync(Filial? filial = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Funcionarios.AsNoTracking().Include(f => f.Setor).Include(f => f.Horarios).AsQueryable();
        if (filial is { } fil) q = q.Where(f => f.Filial == fil);
        return await q.OrderBy(f => f.Nome).ToListAsync(ct);
    }

    public async Task<Funcionario> SalvarAsync(Funcionario funcionario, CancellationToken ct = default)
    {
        var nome = (funcionario.Nome ?? "").Trim();
        if (nome.Length == 0) throw new CadastroException("Informe o nome do funcionário.");
        if (!Filiais.Existe(funcionario.Filial)) throw new CadastroException("Selecione uma filial válida.");
        if (funcionario.FilialExtra is { } extra)
        {
            if (!Filiais.Existe(extra)) throw new CadastroException("Selecione uma filial extra válida.");
            if (extra == funcionario.Filial) throw new CadastroException("A filial extra deve ser diferente da filial do funcionário.");
        }
        if (funcionario.HorarioPersonalizado) ValidarHorarios(funcionario.Horarios);

        await using var db = await factory.CreateDbContextAsync(ct);
        var setor = await db.Setores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == funcionario.SetorId, ct)
            ?? throw new CadastroException("Selecione um setor.");
        if (setor.Filial != funcionario.Filial)
            throw new CadastroException("O setor selecionado não pertence à filial do funcionário.");

        Funcionario alvo;
        if (funcionario.Id == 0)
        {
            alvo = new Funcionario();
            db.Funcionarios.Add(alvo);
        }
        else
        {
            alvo = await db.Funcionarios.Include(f => f.Horarios).FirstOrDefaultAsync(f => f.Id == funcionario.Id, ct)
                ?? throw new CadastroException("Funcionário não encontrado.");
        }
        // Trocou a filial ou a filial extra: as coberturas antigas deixam de valer e ele volta para a própria filial nesses dias.
        if (alvo.Id != 0 && (alvo.Filial != funcionario.Filial || alvo.FilialExtra != funcionario.FilialExtra))
            await db.EscalasPadrao.Where(p => p.FuncionarioId == alvo.Id && p.FilialCobertura != null)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.FilialCobertura, (Filial?)null), ct);
        alvo.Nome = nome;
        alvo.Filial = funcionario.Filial;
        alvo.FilialExtra = funcionario.FilialExtra;
        alvo.SetorId = funcionario.SetorId;
        alvo.Ativo = funcionario.Ativo;
        alvo.Foto = funcionario.Foto;
        alvo.HorarioPersonalizado = funcionario.HorarioPersonalizado;
        // Desativado: os horários guardados ficam como estão (voltam a valer se a opção for reativada).
        if (funcionario.HorarioPersonalizado) AtualizarHorarios(alvo, funcionario.Horarios);
        await db.SaveChangesAsync(ct);
        return new Funcionario
        {
            Id = alvo.Id, Nome = alvo.Nome, Filial = alvo.Filial, FilialExtra = alvo.FilialExtra, SetorId = alvo.SetorId,
            Ativo = alvo.Ativo, Foto = alvo.Foto, HorarioPersonalizado = alvo.HorarioPersonalizado,
            Horarios = alvo.Horarios.Select(h => new HorarioFuncionario
            {
                Id = h.Id, FuncionarioId = alvo.Id, DiaSemana = h.DiaSemana, Inicio = h.Inicio, Fim = h.Fim,
            }).ToList(),
        };
    }

    private static readonly string[] NomesDias = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

    private static void ValidarHorarios(IReadOnlyCollection<HorarioFuncionario> horarios)
    {
        if (horarios.Count == 0)
            throw new CadastroException("Horário personalizado ativo: defina o horário de pelo menos um dia, ou desative a opção.");
        if (horarios.Select(h => h.DiaSemana).Distinct().Count() != horarios.Count)
            throw new CadastroException("Dia da semana repetido no horário personalizado.");
        foreach (var h in horarios)
            if (RegraHoras.Validar(h.Inicio, h.Fim) is { } erro)
                throw new CadastroException($"{NomesDias[(int)h.DiaSemana]}: {erro}");
    }

    /// <summary>Deixa os horários do funcionário iguais aos informados (um por dia da semana).</summary>
    private static void AtualizarHorarios(Funcionario alvo, IReadOnlyCollection<HorarioFuncionario> novos)
    {
        var porDia = novos.ToDictionary(h => h.DiaSemana);
        alvo.Horarios.RemoveAll(h => !porDia.ContainsKey(h.DiaSemana));
        foreach (var n in novos)
        {
            var atual = alvo.Horarios.FirstOrDefault(h => h.DiaSemana == n.DiaSemana);
            if (atual is null)
                alvo.Horarios.Add(new HorarioFuncionario { DiaSemana = n.DiaSemana, Inicio = n.Inicio, Fim = n.Fim });
            else
                (atual.Inicio, atual.Fim) = (n.Inicio, n.Fim);
        }
    }

    public async Task ExcluirAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var f = await db.Funcionarios.FindAsync([id], ct) ?? throw new CadastroException("Funcionário não encontrado.");
        if (await db.Escalas.AnyAsync(e => e.FuncionarioId == id, ct))
            throw new CadastroException("O funcionário já tem escalas lançadas. Desative-o em vez de excluir.");
        db.Funcionarios.Remove(f);
        await db.SaveChangesAsync(ct);
    }
}
