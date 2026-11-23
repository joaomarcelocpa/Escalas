using Escalas.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Persistencia;

public class EscalasDbContext(DbContextOptions<EscalasDbContext> options) : DbContext(options)
{
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Setor> Setores => Set<Setor>();
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<DiaFilial> DiasFilial => Set<DiaFilial>();
    public DbSet<Escala> Escalas => Set<Escala>();
    public DbSet<EscalaPadrao> EscalasPadrao => Set<EscalaPadrao>();
    public DbSet<Feriado> Feriados => Set<Feriado>();

    /// <summary>
    /// Filiais são fixas (enum <see cref="Filial"/>), gravadas como número na coluna "Filial".
    /// A tabela antiga de filiais foi preservada como "FiliaisAntigas" pela migration FiliaisFixas (fora do modelo).
    /// </summary>
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Empresa>(e =>
        {
            e.ToTable("Empresa", t => t.HasCheckConstraint("CK_Empresa_UnicaLinha", "Id = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nome).IsRequired();
        });

        b.Entity<Setor>(e =>
        {
            e.HasIndex(x => x.Filial);
            e.Property(x => x.Nome).IsRequired();
        });

        b.Entity<Funcionario>(e =>
        {
            e.HasIndex(x => x.Filial);
            e.HasOne(x => x.Setor).WithMany().HasForeignKey(x => x.SetorId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Nome).IsRequired();
        });

        b.Entity<DiaFilial>(e =>
        {
            e.HasIndex(x => new { x.Filial, x.Data }).IsUnique();
        });

        b.Entity<Escala>(e =>
        {
            e.HasIndex(x => new { x.FuncionarioId, x.Data }).IsUnique();
            e.HasOne(x => x.Funcionario).WithMany().HasForeignKey(x => x.FuncionarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EscalaPadrao>(e =>
        {
            e.HasIndex(x => new { x.FuncionarioId, x.DiaSemana }).IsUnique();
            e.HasOne(x => x.Funcionario).WithMany().HasForeignKey(x => x.FuncionarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Feriado>(e =>
        {
            e.HasIndex(x => x.Data);
            e.Property(x => x.Nome).IsRequired();
            e.HasIndex(x => x.Filial);
        });
    }
}
