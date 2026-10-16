using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Cadastros;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

public partial class FuncionariosViewModel : CadastroViewModelBase
{
    private readonly IFilialService _filiais;
    private readonly ISetorService _setores;
    private readonly IFuncionarioService _servico;
    private int _id;

    public ObservableCollection<Filial> Filiais { get; } = [];
    public ObservableCollection<Setor> Setores { get; } = [];
    public ObservableCollection<Funcionario> Itens { get; } = [];

    [ObservableProperty] public partial Filial? FilialSelecionada { get; set; }
    [ObservableProperty] public partial Funcionario? Selecionado { get; set; }
    [ObservableProperty] public partial Setor? SetorSelecionado { get; set; }
    [ObservableProperty] public partial string Nome { get; set; } = "";
    [ObservableProperty] public partial bool Ativo { get; set; } = true;

    public FuncionariosViewModel(IFilialService filiais, ISetorService setores, IFuncionarioService servico)
    {
        _filiais = filiais;
        _setores = setores;
        _servico = servico;
        _ = ExecutarAsync(async () =>
        {
            foreach (var f in await _filiais.ListarAsync()) Filiais.Add(f);
            FilialSelecionada = Filiais.FirstOrDefault();
        });
    }

    partial void OnFilialSelecionadaChanged(Filial? value) => _ = ExecutarAsync(async () =>
    {
        Setores.Clear();
        if (value is not null)
            foreach (var s in await _setores.ListarAsync(value.Id)) Setores.Add(s);
        await CarregarAsync(null);
    });

    partial void OnSelecionadoChanged(Funcionario? value)
    {
        _id = value?.Id ?? 0;
        Nome = value?.Nome ?? "";
        Ativo = value?.Ativo ?? true;
        SetorSelecionado = value is null ? null : Setores.FirstOrDefault(s => s.Id == value.SetorId);
    }

    private async Task CarregarAsync(int? selecionarId)
    {
        Itens.Clear();
        if (FilialSelecionada is null) return;
        foreach (var f in await _servico.ListarAsync(FilialSelecionada.Id)) Itens.Add(f);
        Selecionado = selecionarId is int id ? Itens.FirstOrDefault(f => f.Id == id) : null;
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
        var salvo = await _servico.SalvarAsync(new Funcionario
        {
            Id = _id,
            Nome = Nome,
            FilialId = FilialSelecionada.Id,
            SetorId = SetorSelecionado?.Id ?? 0,
            Ativo = Ativo,
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
