using Escalas.Desktop.Navegacao;
using Escalas.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Escalas.Desktop.Modulos;

public static class CadastrosUiModulo
{
    /// <summary>Registra ViewModels do módulo e acrescenta suas entradas ao menu.</summary>
    public static void Registrar(IServiceCollection services, List<ItemNavegacao> menu)
    {
        services.AddTransient<SetoresViewModel>();
        services.AddTransient<FuncionariosViewModel>();

        menu.Add(new ItemNavegacao("Setores", typeof(SetoresViewModel), Icones.Setores));
        menu.Add(new ItemNavegacao("Funcionários", typeof(FuncionariosViewModel), Icones.Funcionarios));
    }
}
