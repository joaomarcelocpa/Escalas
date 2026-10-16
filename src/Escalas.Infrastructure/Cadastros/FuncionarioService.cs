using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Cadastros;

public class FuncionarioService(IDbContextFactory<EscalasDbContext> factory) : IFuncionarioService
{
    public async Task<IReadOnlyList<Funcionario>> ListarAsync(int? filialId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Funcionarios.AsNoTracking().Include(f => f.Setor).AsQueryable();
        if (filialId is int fid) q = q.Where(f => f.FilialId == fid);
        return await q.OrderBy(f => f.Nome).ToListAsync(ct);
    }

    public async Task<Funcionario> SalvarAsync(Funcionario funcionario, CancellationToken ct = default)
    {
        var nome = (funcionario.Nome ?? "").Trim();
        if (nome.Length == 0) throw new CadastroException("Informe o nome do funcionário.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var setor = await db.Setores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == funcionario.SetorId, ct)
            ?? throw new CadastroException("Selecione um setor.");
        if (setor.FilialId != funcionario.FilialId)
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
        alvo.Nome = nome;
        alvo.FilialId = funcionario.FilialId;
        alvo.SetorId = funcionario.SetorId;
        alvo.Ativo = funcionario.Ativo;
        alvo.Foto = funcionario.Foto;
        await db.SaveChangesAsync(ct);
        return new Funcionario { Id = alvo.Id, Nome = alvo.Nome, FilialId = alvo.FilialId, SetorId = alvo.SetorId, Ativo = alvo.Ativo, Foto = alvo.Foto };
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
