using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Cadastros;
using Escalas.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Tests.Cadastros;

public sealed class CadastrosTests : IDisposable
{
    private sealed class Factory(DbContextOptions<EscalasDbContext> o) : IDbContextFactory<EscalasDbContext>
    {
        public EscalasDbContext CreateDbContext() => new(o);
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly Factory _factory;
    private readonly FilialService _filiais;
    private readonly SetorService _setores;
    private readonly FuncionarioService _funcs;

    public CadastrosTests()
    {
        _conn.Open();
        var options = new DbContextOptionsBuilder<EscalasDbContext>().UseSqlite(_conn).Options;
        _factory = new Factory(options);
        using (var db = _factory.CreateDbContext()) db.Database.EnsureCreated();
        _filiais = new(_factory);
        _setores = new(_factory);
        _funcs = new(_factory);
    }

    public void Dispose() => _conn.Dispose();

    private Task<Filial> NovaFilial(string nome = "Centro") =>
        _filiais.SalvarAsync(new Filial { Nome = nome, Cidade = "Recife", UF = "pe" });

    [Fact]
    public async Task Filial_normaliza_uf_e_valida_campos()
    {
        var f = await NovaFilial();
        Assert.Equal("PE", f.UF);
        await Assert.ThrowsAsync<CadastroException>(() => _filiais.SalvarAsync(new Filial { Nome = "", Cidade = "X", UF = "PE" }));
        await Assert.ThrowsAsync<CadastroException>(() => _filiais.SalvarAsync(new Filial { Nome = "A", Cidade = "X", UF = "PER" }));
    }

    [Fact]
    public async Task Filial_com_setor_nao_pode_ser_excluida()
    {
        var f = await NovaFilial();
        await _setores.SalvarAsync(new Setor { FilialId = f.Id, Nome = "Caixa", MinimoFuncionarios = 2 });
        await Assert.ThrowsAsync<CadastroException>(() => _filiais.ExcluirAsync(f.Id));
    }

    [Fact]
    public async Task Setor_rejeita_minimo_negativo_e_nome_duplicado()
    {
        var f = await NovaFilial();
        await Assert.ThrowsAsync<CadastroException>(() => _setores.SalvarAsync(new Setor { FilialId = f.Id, Nome = "A", MinimoFuncionarios = -1 }));
        await _setores.SalvarAsync(new Setor { FilialId = f.Id, Nome = "Caixa" });
        await Assert.ThrowsAsync<CadastroException>(() => _setores.SalvarAsync(new Setor { FilialId = f.Id, Nome = "Caixa" }));
    }

    [Fact]
    public async Task Funcionario_exige_setor_da_mesma_filial()
    {
        var a = await NovaFilial("A");
        var b = await NovaFilial("B");
        var setorB = await _setores.SalvarAsync(new Setor { FilialId = b.Id, Nome = "Estoque" });
        await Assert.ThrowsAsync<CadastroException>(() =>
            _funcs.SalvarAsync(new Funcionario { Nome = "Ana", FilialId = a.Id, SetorId = setorB.Id }));
    }

    [Fact]
    public async Task Funcionario_e_setor_com_dependencias_nao_sao_excluidos()
    {
        var f = await NovaFilial();
        var s = await _setores.SalvarAsync(new Setor { FilialId = f.Id, Nome = "Caixa" });
        var func = await _funcs.SalvarAsync(new Funcionario { Nome = "Ana", FilialId = f.Id, SetorId = s.Id });

        await Assert.ThrowsAsync<CadastroException>(() => _setores.ExcluirAsync(s.Id));

        await using (var db = _factory.CreateDbContext())
        {
            db.Escalas.Add(new Escala { FuncionarioId = func.Id, Data = new DateOnly(2026, 1, 5) });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<CadastroException>(() => _funcs.ExcluirAsync(func.Id));
    }

    [Fact]
    public async Task Fluxo_completo_cria_lista_edita_e_exclui()
    {
        var f = await NovaFilial();
        var s = await _setores.SalvarAsync(new Setor { FilialId = f.Id, Nome = "Caixa", MinimoFuncionarios = 1 });
        var func = await _funcs.SalvarAsync(new Funcionario { Nome = " Ana ", FilialId = f.Id, SetorId = s.Id });
        Assert.Equal("Ana", func.Nome);

        func.Ativo = false;
        await _funcs.SalvarAsync(func);
        var lista = await _funcs.ListarAsync(f.Id);
        Assert.Single(lista);
        Assert.False(lista[0].Ativo);
        Assert.Equal("Caixa", lista[0].Setor!.Nome);

        await _funcs.ExcluirAsync(func.Id);
        await _setores.ExcluirAsync(s.Id);
        await _filiais.ExcluirAsync(f.Id);
        Assert.Empty(await _filiais.ListarAsync());
    }
}
