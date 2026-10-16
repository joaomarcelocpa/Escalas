using Escalas.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Persistencia;

public class EscalasDbContext(DbContextOptions<EscalasDbContext> options) : DbContext(options)
{
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Filial> Filiais => Set<Filial>();
    public DbSet<Setor> Setores => Set<Setor>();
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<DiaFilial> DiasFilial => Set<DiaFilial>();
    public DbSet<Escala> Escalas => Set<Escala>();
    public DbSet<EscalaPadrao> EscalasPadrao => Set<EscalaPadrao>();
    public DbSet<Feriado> Feriados => Set<Feriado>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Empresa>(e =>
        {
            e.ToTable("Empresa", t => t.HasCheckConstraint("CK_Empresa_UnicaLinha", "Id = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nome).IsRequired();
        });

        b.Entity<Usuario>(e =>
        {
            e.HasIndex(x => x.Login).IsUnique();
            e.Property(x => x.Login).IsRequired();
            e.Property(x => x.SenhaHash).IsRequired();
        });

        b.Entity<Filial>(e =>
        {
            e.Property(x => x.Nome).IsRequired();
            e.Property(x => x.UF).HasMaxLength(2);
        });

        b.Entity<Setor>(e =>
        {
            e.HasOne(x => x.Filial).WithMany(f => f.Setores).HasForeignKey(x => x.FilialId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Nome).IsRequired();
        });

        b.Entity<Funcionario>(e =>
        {
            e.HasOne(x => x.Filial).WithMany().HasForeignKey(x => x.FilialId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Setor).WithMany().HasForeignKey(x => x.SetorId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Nome).IsRequired();
        });

        b.Entity<DiaFilial>(e =>
        {
            e.HasIndex(x => new { x.FilialId, x.Data }).IsUnique();
            e.HasOne(x => x.Filial).WithMany().HasForeignKey(x => x.FilialId).OnDelete(DeleteBehavior.Cascade);
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
            e.HasOne(x => x.Filial).WithMany().HasForeignKey(x => x.FilialId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
