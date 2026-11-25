using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Escalas.Application.Calendario;
using Escalas.Desktop.ViewModels;
using Escalas.Desktop.Views;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.Servicos;

public class DialogosHorario(IExpedienteService expedientes, IHorasService horas) : IDialogosHorario
{
    private static Window? Dono =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<bool> EditarExpedienteAsync()
    {
        if (Dono is not { } dono) return false;
        var vm = new ExpedienteSemanaViewModel(expedientes);
        await vm.CarregarAsync();
        return await EscalaInterface.Preparar(new ExpedienteWindow { DataContext = vm }, dono).ShowDialog<bool>(dono);
    }

    public async Task MostrarHorasFuncionarioAsync(int funcionarioId, DateOnly inicioSemana, Filial filial)
    {
        if (Dono is not { } dono) return;
        var dto = await horas.ObterFuncionarioAsync(funcionarioId, inicioSemana, filial);
        if (dto is null) return;
        await EscalaInterface.Preparar(new HorasFuncionarioWindow { DataContext = new HorasFuncionarioViewModel(dto) }, dono).ShowDialog(dono);
    }

    public async Task MostrarResumoSemanalAsync(DateOnly inicioSemana)
    {
        if (Dono is not { } dono) return;
        var dto = await horas.ObterResumoAsync(inicioSemana);
        await EscalaInterface.Preparar(new ResumoSemanalWindow { DataContext = new ResumoSemanalViewModel(dto) }, dono).ShowDialog(dono);
    }
}
