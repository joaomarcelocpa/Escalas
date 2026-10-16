using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Views;

public partial class EscalaSemanalView : UserControl
{
    public EscalaSemanalView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, AoPressionar, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, AoArrastarSobre);
        AddHandler(DragDrop.DropEvent, AoSoltar);
    }

    /// <summary>Payload: "id|diaOrigem" (diaOrigem vazio = veio da lista de disponíveis).</summary>
    private async void AoPressionar(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Control origem || origem.DataContext is not FuncionarioPadraoItem item) return;
        for (Visual? v = origem; v is not null && v != this; v = v.GetVisualParent())
            if (v is Button) return;

        var dado = new DataTransfer();
        dado.Add(DataTransferItem.CreateText($"{item.Id}|{(item.Origem is { } o ? (int)o : "")}"));
        await DragDrop.DoDragDropAsync(e, dado, DragDropEffects.Move);
    }

    private void AoArrastarSobre(object? sender, DragEventArgs e)
    {
        e.DragEffects = DestinoDe(e) is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private async void AoSoltar(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not EscalaSemanalViewModel vm) return;
        var partes = e.DataTransfer.TryGetText()?.Split('|');
        if (partes is not { Length: 2 } || !int.TryParse(partes[0], out var id)) return;
        DayOfWeek? origem = int.TryParse(partes[1], out var o) ? (DayOfWeek)o : null;

        switch (DestinoDe(e))
        {
            case DiaPadraoViewModel dia:
                await vm.SoltarEmDiaAsync(id, origem, dia.Dia);
                break;
            case "disponiveis":
                await vm.SoltarEmDisponiveisAsync(id, origem);
                break;
        }
    }

    /// <summary>Sobe a árvore visual: a zona "disponiveis" (Tag) vence o dia que a contém.</summary>
    private static object? DestinoDe(DragEventArgs e)
    {
        for (var c = e.Source as Visual; c is not null; c = c.GetVisualParent())
        {
            if (c is Control { Tag: "disponiveis" }) return "disponiveis";
            if (c is Control { DataContext: DiaPadraoViewModel dia }) return dia;
        }
        return null;
    }
}
