using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Escalas.Desktop.ViewModels;

namespace Escalas.Desktop.Views;

public partial class CalendarioView : UserControl
{
    private const string FormatoData = "yyyy-MM-dd";

    public CalendarioView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, AoPressionar, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, AoArrastarSobre);
        AddHandler(DragDrop.DropEvent, AoSoltar);
    }

    /// <summary>Payload: "id|origem" (origem vazia = veio da lista de disponíveis).</summary>
    private async void AoPressionar(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Control origem) return;
        if (origem.DataContext is not FuncionarioItem item) return;
        for (Visual? v = origem; v is not null && v != this; v = v.GetVisualParent())
            if (v is Button) return; // clique em botão do cartão (ex.: remover) não inicia arraste

        var dado = new DataTransfer();
        dado.Add(DataTransferItem.CreateText($"{item.Id}|{item.Origem?.ToString(FormatoData, CultureInfo.InvariantCulture)}"));
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
        if (DataContext is not CalendarioViewModel vm) return;
        if (!TentarLerPayload(e, out var funcionarioId, out var origem)) return;

        switch (DestinoDe(e))
        {
            case DiaViewModel dia:
                await vm.SoltarEmDiaAsync(funcionarioId, origem, dia.Data);
                break;
            case "disponiveis":
                await vm.SoltarEmDisponiveisAsync(funcionarioId, origem);
                break;
        }
    }

    /// <summary>Sobe a árvore visual a partir do alvo: a zona "disponiveis" (Tag) vence o dia que a contém.</summary>
    private static object? DestinoDe(DragEventArgs e)
    {
        for (var c = e.Source as Visual; c is not null; c = c.GetVisualParent())
        {
            if (c is Control { Tag: "disponiveis" }) return "disponiveis";
            if (c is Control { DataContext: DiaViewModel dia }) return dia;
        }
        return null;
    }

    private static bool TentarLerPayload(DragEventArgs e, out int funcionarioId, out DateOnly? origem)
    {
        funcionarioId = 0;
        origem = null;

        var partes = e.DataTransfer.TryGetText()?.Split('|');
        if (partes is not { Length: 2 } || !int.TryParse(partes[0], out funcionarioId)) return false;

        if (partes[1].Length > 0)
        {
            if (!DateOnly.TryParseExact(partes[1], FormatoData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                return false;
            origem = data;
        }
        return true;
    }
}
