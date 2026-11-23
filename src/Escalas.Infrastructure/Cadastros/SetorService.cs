using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Cadastros;

public class SetorService(IDbContextFactory<EscalasDbContext> factory) : ISetorService
{
    public async Task<IReadOnlyList<Setor>> ListarAsync(Filial filial, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Setores.AsNoTracking().Where(s => s.Filial == filial).OrderBy(s => s.Nome).ToListAsync(ct);
    }

    public async Task<Setor> SalvarAsync(Setor setor, CancellationToken ct = default)
    {
        var nome = (setor.Nome ?? "").Trim();
        if (nome.Length == 0) throw new CadastroException("Informe o nome do setor.");
        if (setor.MinimoFuncionarios < 0) throw new CadastroException("O mínimo de funcionários não pode ser negativo.");

        if (!Filiais.Existe(setor.Filial)) throw new CadastroException("Selecione uma filial válida.");

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Setores.AnyAsync(s => s.Filial == setor.Filial && s.Id != setor.Id && s.Nome == nome, ct))
            throw new CadastroException("Já existe um setor com esse nome nesta filial.");

        Setor alvo;
        if (setor.Id == 0)
        {
            alvo = new Setor { Filial = setor.Filial };
            db.Setores.Add(alvo);
        }
        else
        {
            alvo = await db.Setores.FindAsync([setor.Id], ct) ?? throw new CadastroException("Setor não encontrado.");
            if (alvo.Filial != setor.Filial && await db.Funcionarios.AnyAsync(f => f.SetorId == alvo.Id, ct))
                throw new CadastroException("O setor possui funcionários e não pode mudar de filial.");
            alvo.Filial = setor.Filial;
        }
        alvo.Nome = nome;
        alvo.MinimoFuncionarios = setor.MinimoFuncionarios;
        await db.SaveChangesAsync(ct);
        return new Setor { Id = alvo.Id, Filial = alvo.Filial, Nome = alvo.Nome, MinimoFuncionarios = alvo.MinimoFuncionarios };
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
