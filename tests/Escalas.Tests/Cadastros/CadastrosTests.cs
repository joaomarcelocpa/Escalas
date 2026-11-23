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
    private readonly SetorService _setores;
    private readonly FuncionarioService _funcs;

    public CadastrosTests()
    {
        _conn.Open();
        var options = new DbContextOptionsBuilder<EscalasDbContext>().UseSqlite(_conn).Options;
        _factory = new Factory(options);
        using (var db = _factory.CreateDbContext()) db.Database.EnsureCreated();
        _setores = new(_factory);
        _funcs = new(_factory);
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void Filiais_sao_fixas_na_ordem_combinada()
    {
        Assert.Equal(["Matriz", "Matozinhos", "FC", "Interiores", "Depósito"], Filiais.Todas.Select(f => f.Nome));
        Assert.Equal([1, 2, 3, 4, 5], Filiais.Todas.Select(f => (int)f.Id));
    }

    [Theory]
    [InlineData("Matriz", Filial.Matriz)]
    [InlineData("  MATOZINHOS ", Filial.Matozinhos)]
    [InlineData("fc", Filial.FC)]
    [InlineData("Interiores", Filial.Interiores)]
    [InlineData("Depósito", Filial.Deposito)]
    [InlineData("DEPOSITO", Filial.Deposito)]
    [InlineData("depÓsito", Filial.Deposito)]
    public void Identificar_ignora_maiusculas_e_acentos(string nome, Filial esperada) =>
        Assert.Equal(esperada, Filiais.Identificar(nome));

    [Theory]
    [InlineData("Centro")]
    [InlineData("")]
    [InlineData(null)]
    public void Identificar_devolve_nulo_quando_nao_corresponde(string? nome) => Assert.Null(Filiais.Identificar(nome));

    [Fact]
    public async Task Setor_e_funcionario_rejeitam_filial_inexistente()
    {
        await Assert.ThrowsAsync<CadastroException>(() => _setores.SalvarAsync(new Setor { Filial = (Filial)99, Nome = "Caixa" }));
        var s = await _setores.SalvarAsync(new Setor { Filial = Filial.Matriz, Nome = "Caixa" });
        await Assert.ThrowsAsync<CadastroException>(() =>
            _funcs.SalvarAsync(new Funcionario { Nome = "Ana", Filial = (Filial)99, SetorId = s.Id }));
    }

    [Fact]
    public async Task Setor_rejeita_minimo_negativo_e_nome_duplicado()
    {
        var f = Filial.Matriz;
        await Assert.ThrowsAsync<CadastroException>(() => _setores.SalvarAsync(new Setor { Filial = f, Nome = "A", MinimoFuncionarios = -1 }));
        await _setores.SalvarAsync(new Setor { Filial = f, Nome = "Caixa" });
        await Assert.ThrowsAsync<CadastroException>(() => _setores.SalvarAsync(new Setor { Filial = f, Nome = "Caixa" }));
    }

    [Fact]
    public async Task Funcionario_exige_setor_da_mesma_filial()
    {
        var a = Filial.Matriz;
        var b = Filial.Deposito;
        var setorB = await _setores.SalvarAsync(new Setor { Filial = b, Nome = "Estoque" });
        await Assert.ThrowsAsync<CadastroException>(() =>
            _funcs.SalvarAsync(new Funcionario { Nome = "Ana", Filial = a, SetorId = setorB.Id }));
    }

    [Fact]
    public async Task Funcionario_e_setor_com_dependencias_nao_sao_excluidos()
    {
        var f = Filial.Interiores;
        var s = await _setores.SalvarAsync(new Setor { Filial = f, Nome = "Caixa" });
        var func = await _funcs.SalvarAsync(new Funcionario { Nome = "Ana", Filial = f, SetorId = s.Id });

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
        var f = Filial.Interiores;
        var s = await _setores.SalvarAsync(new Setor { Filial = f, Nome = "Caixa", MinimoFuncionarios = 1 });
        var func = await _funcs.SalvarAsync(new Funcionario { Nome = " Ana ", Filial = f, SetorId = s.Id });
        Assert.Equal("Ana", func.Nome);

        func.Ativo = false;
        await _funcs.SalvarAsync(func);
        var lista = await _funcs.ListarAsync(f);
        Assert.Single(lista);
        Assert.False(lista[0].Ativo);
        Assert.Equal("Caixa", lista[0].Setor!.Nome);

        await _funcs.ExcluirAsync(func.Id);
        await _setores.ExcluirAsync(s.Id);
        Assert.Empty(await _setores.ListarAsync(f));
    }
}
