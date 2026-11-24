using Escalas.Application.Calendario;
using Escalas.Infrastructure.Calendario;
using Escalas.Infrastructure.Exportacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Escalas.Infrastructure.Modulos;

public static class CalendarioModulo
{
    /// <summary>Registra os serviços do módulo Calendario.</summary>
    public static IServiceCollection AdicionarCalendario(this IServiceCollection services)
    {
        services.AddTransient<IEscalaService, EscalaService>();
        services.AddTransient<IEscalaPadraoService, EscalaPadraoService>();
        services.AddTransient<IExpedienteService, ExpedienteService>();
        services.AddTransient<IHorasService, HorasService>();
        services.AddTransient<IExportacaoEscalaService, ExportacaoEscalaService>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
