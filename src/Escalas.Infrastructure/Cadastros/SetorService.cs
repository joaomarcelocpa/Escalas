using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Cadastros;

public class SetorService(IDbContextFactory<EscalasDbContext> factory) : ISetorService
{
    public async Task<IReadOnlyList<Setor>> ListarAsync(int filialId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Setores.AsNoTracking().Where(s => s.FilialId == filialId).OrderBy(s => s.Nome).ToListAsync(ct);
    }

    public async Task<Setor> SalvarAsync(Setor setor, CancellationToken ct = default)
    {
        var nome = (setor.Nome ?? "").Trim();
        if (nome.Length == 0) throw new CadastroException("Informe o nome do setor.");
        if (setor.MinimoFuncionarios < 0) throw new CadastroException("O mínimo de funcionários não pode ser negativo.");

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Filiais.AnyAsync(f => f.Id == setor.FilialId, ct))
            throw new CadastroException("Selecione uma filial válida.");
        if (await db.Setores.AnyAsync(s => s.FilialId == setor.FilialId && s.Id != setor.Id && s.Nome == nome, ct))
            throw new CadastroException("Já existe um setor com esse nome nesta filial.");

        Setor alvo;
        if (setor.Id == 0)
        {
            alvo = new Setor { FilialId = setor.FilialId };
            db.Setores.Add(alvo);
        }
        else
        {
            alvo = await db.Setores.FindAsync([setor.Id], ct) ?? throw new CadastroException("Setor não encontrado.");
            if (alvo.FilialId != setor.FilialId && await db.Funcionarios.AnyAsync(f => f.SetorId == alvo.Id, ct))
                throw new CadastroException("O setor possui funcionários e não pode mudar de filial.");
            alvo.FilialId = setor.FilialId;
        }
        alvo.Nome = nome;
        alvo.MinimoFuncionarios = setor.MinimoFuncionarios;
        await db.SaveChangesAsync(ct);
        return new Setor { Id = alvo.Id, FilialId = alvo.FilialId, Nome = alvo.Nome, MinimoFuncionarios = alvo.MinimoFuncionarios };
    }

    public async Task ExcluirAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var setor = await db.Setores.FindAsync([id], ct) ?? throw new CadastroException("Setor não encontrado.");
        if (await db.Funcionarios.AnyAsync(f => f.SetorId == id, ct))
            throw new CadastroException("O setor possui funcionários. Mova-os para outro setor antes de excluir.");
        db.Setores.Remove(setor);
        await db.SaveChangesAsync(ct);
    }
}
