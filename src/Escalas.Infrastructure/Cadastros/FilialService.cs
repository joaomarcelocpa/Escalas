using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Cadastros;

public class FilialService(IDbContextFactory<EscalasDbContext> factory) : IFilialService
{
    public async Task<IReadOnlyList<Filial>> ListarAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Filiais.AsNoTracking().OrderBy(f => f.Nome).ToListAsync(ct);
    }

    public async Task<Filial> SalvarAsync(Filial filial, CancellationToken ct = default)
    {
        var nome = (filial.Nome ?? "").Trim();
        var cidade = (filial.Cidade ?? "").Trim();
        var uf = (filial.UF ?? "").Trim().ToUpperInvariant();
        if (nome.Length == 0) throw new CadastroException("Informe o nome da filial.");
        if (cidade.Length == 0) throw new CadastroException("Informe a cidade da filial.");
        if (uf.Length != 2 || !uf.All(char.IsLetter)) throw new CadastroException("A UF deve ter 2 letras.");

        await using var db = await factory.CreateDbContextAsync(ct);
        Filial alvo;
        if (filial.Id == 0)
        {
            alvo = new Filial();
            db.Filiais.Add(alvo);
        }
        else
        {
            alvo = await db.Filiais.FindAsync([filial.Id], ct) ?? throw new CadastroException("Filial não encontrada.");
        }
        alvo.Nome = nome;
        alvo.Cidade = cidade;
        alvo.UF = uf;
        await db.SaveChangesAsync(ct);
        return new Filial { Id = alvo.Id, Nome = alvo.Nome, Cidade = alvo.Cidade, UF = alvo.UF };
    }

    public async Task ExcluirAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var filial = await db.Filiais.FindAsync([id], ct) ?? throw new CadastroException("Filial não encontrada.");
        if (await db.Setores.AnyAsync(s => s.FilialId == id, ct))
            throw new CadastroException("A filial possui setores. Exclua ou mova os setores antes.");
        if (await db.Funcionarios.AnyAsync(f => f.FilialId == id, ct))
            throw new CadastroException("A filial possui funcionários e não pode ser excluída.");
        db.Filiais.Remove(filial);
        await db.SaveChangesAsync(ct);
    }
}
