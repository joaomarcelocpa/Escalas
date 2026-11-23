using Escalas.Application.Nucleo;
using Escalas.Infrastructure.Nucleo;
using Escalas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Infrastructure.Modulos;

public static class NucleoModulo
{
    /// <summary>Serviços do núcleo: caminhos, chave, banco cifrado, backup e inicialização.</summary>
    public static IServiceCollection AdicionarNucleo(this IServiceCollection services, CaminhosApp? caminhos = null, BackupOpcoes? backup = null)
    {
        BancoFabrica.Iniciar();
        services.AddSingleton(caminhos ?? new CaminhosApp());
        services.AddSingleton(backup ?? new BackupOpcoes());
        services.AddSingleton<IChaveStore, ChaveStore>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IInicializadorBanco, InicializadorBanco>();
        // A chave é lida na primeira criação do contexto (depois do InicializadorBanco importá-la).
        services.AddDbContextFactory<EscalasDbContext>((sp, o) =>
        {
            var c = sp.GetRequiredService<CaminhosApp>();
            var chave = sp.GetRequiredService<IChaveStore>().Obter()
                ?? throw new InvalidOperationException("Chave do banco indisponível. Execute o IInicializadorBanco antes.");
            o.UseSqlite(BancoFabrica.ConnectionString(c.CaminhoBanco, chave));
        });
        return services;
    }
}
