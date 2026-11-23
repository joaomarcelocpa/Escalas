using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Cadastros;
using Avalonia.Media.Imaging;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

public partial class FuncionariosViewModel : CadastroViewModelBase
{
    private readonly ISetorService _setores;
    private readonly IFuncionarioService _servico;
    private int _id;

    /// <summary>Filiais fixas.</summary>
    public IReadOnlyList<FilialOpcao> Filiais { get; } = global::Escalas.Domain.Entidades.Filiais.Todas;
    public ObservableCollection<Setor> Setores { get; } = [];
    public ObservableCollection<Funcionario> Itens { get; } = [];

    [ObservableProperty] public partial FilialOpcao? FilialSelecionada { get; set; }
    [ObservableProperty] public partial Funcionario? Selecionado { get; set; }
    [ObservableProperty] public partial Setor? SetorSelecionado { get; set; }
    [ObservableProperty] public partial string Nome { get; set; } = "";
    [ObservableProperty] public partial bool Ativo { get; set; } = true;
    [ObservableProperty] public partial byte[]? Foto { get; set; }

    /// <summary>Lê a imagem escolhida, reduz para um quadrado pequeno e guarda como PNG (a foto vai dentro do banco).</summary>
    public void DefinirFoto(Stream imagem)
    {
        Erro = null;
        try
        {
            using var reduzida = Bitmap.DecodeToWidth(imagem, 192, BitmapInterpolationMode.HighQuality);
            using var saida = new MemoryStream();
            reduzida.Save(saida, new PngBitmapEncoderOptions());
            Foto = saida.ToArray();
        }
        catch
        {
            Erro = "Não foi possível ler essa imagem. Use um arquivo PNG ou JPG.";
        }
    }

    [RelayCommand]
    private void RemoverFoto() => Foto = null;

    public FuncionariosViewModel(ISetorService setores, IFuncionarioService servico)
    {
        _setores = setores;
        _servico = servico;
        FilialSelecionada = Filiais[0];
    }

    partial void OnFilialSelecionadaChanged(FilialOpcao? value) => _ = ExecutarAsync(async () =>
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
        Foto = value?.Foto;
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
            Filial = FilialSelecionada.Id,
            SetorId = SetorSelecionado?.Id ?? 0,
            Ativo = Ativo,
            Foto = Foto,
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
