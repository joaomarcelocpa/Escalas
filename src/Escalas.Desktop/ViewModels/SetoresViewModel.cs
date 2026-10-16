using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

public partial class SetoresViewModel : CadastroViewModelBase
{
    private readonly IFilialService _filiais;
    private readonly ISetorService _servico;
    private int _id;

    public ObservableCollection<Filial> Filiais { get; } = [];
    public ObservableCollection<Setor> Itens { get; } = [];

    [ObservableProperty] public partial Filial? FilialSelecionada { get; set; }
    [ObservableProperty] public partial Setor? Selecionado { get; set; }
    [ObservableProperty] public partial string Nome { get; set; } = "";
    [ObservableProperty] public partial decimal? Minimo { get; set; } = 0;

    public SetoresViewModel(IFilialService filiais, ISetorService servico)
    {
        _filiais = filiais;
        _servico = servico;
        _ = ExecutarAsync(async () =>
        {
            foreach (var f in await _filiais.ListarAsync()) Filiais.Add(f);
            FilialSelecionada = Filiais.FirstOrDefault();
        });
    }

    partial void OnFilialSelecionadaChanged(Filial? value) => _ = ExecutarAsync(() => CarregarAsync(null));

    partial void OnSelecionadoChanged(Setor? value)
    {
        _id = value?.Id ?? 0;
        Nome = value?.Nome ?? "";
        Minimo = value?.MinimoFuncionarios ?? 0;
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
            FilialId = FilialSelecionada.Id,
            Nome = Nome,
            MinimoFuncionarios = (int)(Minimo ?? 0),
        });
        await CarregarAsync(salvo.Id);
    });

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async () =>
    {
        if (_id == 0) return;
        await _servico.ExcluirAsync(_id);
        await CarregarAsync(null);
    });
}
