using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

public partial class SetoresViewModel : CadastroViewModelBase
{
    private readonly ISetorService _servico;
    private int _id;

    /// <summary>Filiais fixas.</summary>
    public IReadOnlyList<FilialOpcao> Filiais { get; } = global::Escalas.Domain.Entidades.Filiais.Todas;
    public ObservableCollection<Setor> Itens { get; } = [];

    [ObservableProperty] public partial FilialOpcao? FilialSelecionada { get; set; }
    [ObservableProperty] public partial Setor? Selecionado { get; set; }
    [ObservableProperty] public partial string Nome { get; set; } = "";

    public SetoresViewModel(ISetorService servico)
    {
        _servico = servico;
        FilialSelecionada = Filiais[0];
    }

    partial void OnFilialSelecionadaChanged(FilialOpcao? value) => _ = ExecutarAsync(() => CarregarAsync(null));

    partial void OnSelecionadoChanged(Setor? value)
    {
        _id = value?.Id ?? 0;
        Nome = value?.Nome ?? "";
    }

    private async Task CarregarAsync(int? selecionarId)
    {
        Itens.Clear();
        if (FilialSelecionada is null) return;
        foreach (var s in await _servico.ListarAsync(FilialSelecionada.Id)) Itens.Add(s);
        Selecionado = selecionarId is int id ? Itens.FirstOrDefault(s => s.Id == id) : null;
    }

    [RelayCommand]
    private void Novo()
    {
        Erro = null;
        Selecionado = null;
        OnSelecionadoChanged(null);
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        if (FilialSelecionada is null) throw new CadastroException("Selecione uma filial.");
        var salvo = await _servico.SalvarAsync(new Setor
        {
            Id = _id,
            Filial = FilialSelecionada.Id,
            Nome = Nome,
        });
        await CarregarAsync(null);
        Novo();
    });

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async () =>
    {
        if (_id == 0) return;
        await _servico.ExcluirAsync(_id);
        await CarregarAsync(null);
    });
}
