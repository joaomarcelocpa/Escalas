using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Escalas.Infrastructure.Persistencia;

/// <summary>Usada apenas pelas ferramentas `dotnet ef` em tempo de design.</summary>
public class EscalasDbContextFactory : IDesignTimeDbContextFactory<EscalasDbContext>
{
    public EscalasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EscalasDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new EscalasDbContext(options);
    }
}
