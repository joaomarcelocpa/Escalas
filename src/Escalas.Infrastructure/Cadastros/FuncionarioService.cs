using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Cadastros;

public class FuncionarioService(IDbContextFactory<EscalasDbContext> factory) : IFuncionarioService
{
    public async Task<IReadOnlyList<Funcionario>> ListarAsync(Filial? filial = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Funcionarios.AsNoTracking().Include(f => f.Setor).AsQueryable();
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
            alvo = await db.Funcionarios.FindAsync([funcionario.Id], ct) ?? throw new CadastroException("Funcionário não encontrado.");
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
        await db.SaveChangesAsync(ct);
        return new Funcionario { Id = alvo.Id, Nome = alvo.Nome, Filial = alvo.Filial, FilialExtra = alvo.FilialExtra, SetorId = alvo.SetorId, Ativo = alvo.Ativo, Foto = alvo.Foto };
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
