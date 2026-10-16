using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

public partial class FiliaisViewModel : CadastroViewModelBase
{
    private readonly IFilialService _servico;
    private int _id;

    public ObservableCollection<Filial> Itens { get; } = [];

    [ObservableProperty] public partial Filial? Selecionada { get; set; }
    [ObservableProperty] public partial string Nome { get; set; } = "";
    [ObservableProperty] public partial string Cidade { get; set; } = "";
    [ObservableProperty] public partial string UF { get; set; } = "";

    public FiliaisViewModel(IFilialService servico)
    {
        _servico = servico;
        _ = ExecutarAsync(CarregarAsync);
    }

    private async Task CarregarAsync(int? selecionarId = null)
    {
        var lista = await _servico.ListarAsync();
        Itens.Clear();
        foreach (var f in lista) Itens.Add(f);
        Selecionada = selecionarId is int id ? Itens.FirstOrDefault(f => f.Id == id) : null;
    }

    private Task CarregarAsync() => CarregarAsync(null);

    partial void OnSelecionadaChanged(Filial? value)
    {
        _id = value?.Id ?? 0;
        Nome = value?.Nome ?? "";
        Cidade = value?.Cidade ?? "";
        UF = value?.UF ?? "";
    }

    [RelayCommand]
    private void Novo()
    {
        Erro = null;
        Selecionada = null;
        OnSelecionadaChanged(null);
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var salva = await _servico.SalvarAsync(new Filial { Id = _id, Nome = Nome, Cidade = Cidade, UF = UF });
        await CarregarAsync(null);
        Novo();
    });

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async () =>
    {
        if (_id == 0) return;
        await _servico.ExcluirAsync(_id);
        await CarregarAsync();
    });
}
